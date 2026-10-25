using System.Collections.Concurrent;
using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using System.Timers;
using Void.Proxy.Api;
using Void.Proxy.Api.Configurations;
using Void.Proxy.Api.Configurations.Attributes;
using Void.Proxy.Api.Configurations.Exceptions;
using Void.Proxy.Api.Console;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Api.Plugins.Extensions;
using Void.Proxy.Configurations.Serializers;
using Timer = System.Timers.Timer;

namespace Void.Proxy.Configurations;

public class ConfigurationService(ILogger<ConfigurationService> logger, IPluginService plugins, IRunOptions runOptions, IConsoleService console) : BackgroundService, IConfigurationService, IEventListener
{
    private readonly Option<bool> _readOnlyOption = new("--read-only")
    {
        Description = "Disables saving changes to the configuration files"
    };

    private readonly string _configurationsPath = Path.Combine(runOptions.WorkingDirectory, "configs");
    private readonly ConfigurationTomlSerializer _serializer = new();
    private readonly ConcurrentDictionary<string, object> _configurations = [];
    private readonly ConcurrentDictionary<string, DateTime> _updatesCooldown = [];

    // After the plugin's own unloading listeners, which may still read or save its configurations.
    [Subscribe]
    public void OnPluginUnloaded(PluginUnloadedEvent @event)
    {
        var assembly = @event.Plugin.GetType().Assembly;

        _serializer.RemoveAssemblyCache(assembly);

        lock (this)
        {
            for (var i = _configurations.Count - 1; i >= 0; i--)
            {
                var (key, configuration) = _configurations.ElementAt(i);
                var configurationType = configuration.GetType();

                if (configurationType.Assembly != assembly)
                    continue;

                if (!_configurations.Remove(key, out _))
                    throw new InvalidOperationException($"Failed to remove configuration {key}");
            }
        }
    }

    public async ValueTask<TConfiguration> GetAsync<TConfiguration>(CancellationToken cancellationToken = default) where TConfiguration : notnull
    {
        return CastConfiguration<TConfiguration>(await GetAsync(typeof(TConfiguration), cancellationToken));
    }

    public async ValueTask<object> GetAsync(Type configurationType, CancellationToken cancellationToken = default)
    {
        return await GetAsync(key: null, configurationType, cancellationToken);
    }

    public async ValueTask<TConfiguration> GetAsync<TConfiguration>(string? key, CancellationToken cancellationToken = default) where TConfiguration : notnull
    {
        return CastConfiguration<TConfiguration>(await GetAsync(key, typeof(TConfiguration), cancellationToken));
    }

    public async ValueTask<object> GetAsync(string? key, Type configurationType, CancellationToken cancellationToken = default)
    {
        // Tomlet constraint
        if (configurationType.Attributes.HasFlag(TypeAttributes.Sealed) || (!configurationType.Attributes.HasFlag(TypeAttributes.Public) && !configurationType.Attributes.HasFlag(TypeAttributes.NestedPublic)))
            throw new ArgumentException($"Configuration type '{configurationType.Name}' is either sealed or not public. Tomlet requires public, non-sealed types for serialization.");

        var fileName = GetFileName(key, configurationType);

        if (_configurations.TryGetValue(fileName, out var configuration))
            return configuration;

        if (!_configurations.TryAdd(fileName, configuration = await ReadAsync(fileName, configurationType, cancellationToken)))
            throw new InvalidOperationException($"Failed to add configuration {fileName}");

        return configuration;
    }

    internal static TConfiguration CastConfiguration<TConfiguration>(object configuration) where TConfiguration : notnull
    {
        if (configuration is not TConfiguration castedConfiguration)
            throw new InvalidConfigurationException($"Found collision in configuration name: {configuration} != {typeof(TConfiguration)}");

        return castedConfiguration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EnsureConfigurationsPathExists();

        var channel = Channel.CreateUnbounded<EventArgs>();
        var configurationWatcher = new Timer
        {
            Interval = 1000,
            AutoReset = true,
            Enabled = console.TryGetOptionValue(_readOnlyOption, out var readOnly) && !readOnly
        };
        var fileSystemWatcher = new FileSystemWatcher
        {
            Filter = "*.*",
            NotifyFilter = NotifyFilters.LastWrite,
            Path = _configurationsPath,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        configurationWatcher.Elapsed += (_, args) => channel.Writer.TryWrite(args);
        fileSystemWatcher.Created += (_, args) => channel.Writer.TryWrite(args);
        fileSystemWatcher.Deleted += (_, args) => channel.Writer.TryWrite(args);
        fileSystemWatcher.Renamed += (_, args) => channel.Writer.TryWrite(args);
        fileSystemWatcher.Changed += (_, args) => channel.Writer.TryWrite(args);

        var previousConfigurations = new Dictionary<string, string>();

        await foreach (var args in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                fileSystemWatcher.EnableRaisingEvents = false;
                await HandleAsync(args, previousConfigurations, stoppingToken);
            }
            finally
            {
                fileSystemWatcher.EnableRaisingEvents = true;
            }
        }
    }

    // Apart from the loop, whose state lives as long as the service, so the configurations it handles don't outlive their plugins
    private async ValueTask HandleAsync(EventArgs args, Dictionary<string, string> previousConfigurations, CancellationToken stoppingToken)
    {
        switch (args)
        {
            case FileSystemEventArgs fileSystemEventArgs:
                {
                    if (fileSystemEventArgs.ChangeType is not WatcherChangeTypes.Changed)
                        return;

                    // If the changed path is a directory, not a file, skip
                    if (!File.Exists(fileSystemEventArgs.FullPath))
                        return;

                    if (!_configurations.TryGetValue(fileSystemEventArgs.FullPath, out var configuration))
                        return;

                    if (_updatesCooldown.TryGetValue(fileSystemEventArgs.FullPath, out var skipUntilDate) && skipUntilDate > DateTime.Now)
                        return;

                    logger.LogInformation("Configuration {ConfigurationName} changed from disk", GetConfigurationName(configuration.GetType()));

                    var updatedConfiguration = await ReadAsync(fileSystemEventArgs.FullPath, configuration.GetType(), stoppingToken);
                    SwapConfiguration(configuration, updatedConfiguration);
                    break;
                }
            case ElapsedEventArgs:
                {
                    var queue = new Queue<KeyValuePair<string, object>>(_configurations);
                    while (queue.TryDequeue(out var pair))
                    {
                        var (fileName, configuration) = pair;

                        if (_updatesCooldown.TryGetValue(fileName, out var skipUntilDate) && skipUntilDate > DateTime.Now)
                            continue;

                        if (!previousConfigurations.TryGetValue(fileName, out var previousSerializedValue))
                        {
                            var configurationType = configuration.GetType();

                            configuration = await ReadAsync(fileName, configurationType, stoppingToken);
                            previousSerializedValue = _serializer.Serialize(configuration);

                            previousConfigurations.Add(fileName, previousSerializedValue);
                            continue;
                        }

                        var serializedValue = _serializer.Serialize(configuration);
                        if (serializedValue != previousSerializedValue)
                        {
                            logger.LogTrace("Configuration {ConfigurationName} changed in memory", GetConfigurationName(configuration.GetType()));
                            previousConfigurations[fileName] = serializedValue;

                            await WaitFileLockAsync(fileName, stoppingToken);
                            await SaveAsync(fileName, configuration, stoppingToken);
                        }
                    }
                    break;
                }
        }
    }

    private async ValueTask<object> ReadAsync(string fileName, Type configurationType, CancellationToken cancellationToken)
    {
        logger.LogTrace("Loading configuration file {FileName}", fileName);

        if (!File.Exists(fileName))
            await SaveAsync(fileName, configurationType, cancellationToken);

        await WaitFileLockAsync(fileName, cancellationToken);
        var source = await File.ReadAllTextAsync(fileName, cancellationToken);
        var configuration = _serializer.Deserialize(source, configurationType);

        return configuration;
    }

    private async ValueTask SaveAsync(string fileName, object configuration, CancellationToken cancellationToken)
    {
        _updatesCooldown[fileName] = DateTime.Now.AddMilliseconds(500);

        if (configuration is Type)
            logger.LogTrace("Saving default configuration file {FileName}", fileName);
        else
            logger.LogTrace("Saving configuration file {FileName}", fileName);

        EnsureConfigurationsPathExists();

        if (Path.GetDirectoryName(fileName) is { } path && !string.IsNullOrWhiteSpace(path))
            EnsureConfigurationsPathExists(path);

        var value = _serializer.Serialize(configuration);
        await WaitFileLockAsync(fileName, cancellationToken);
        await File.WriteAllTextAsync(fileName, value, cancellationToken);
    }

    private string GetFileName(string? key, Type configurationType)
    {
        var pluginName = GetPluginNameFromConfiguration(configurationType);
        var fileNameBuilder = new StringBuilder();

        if (IsRootConfiguration(configurationType))
        {
            fileNameBuilder.Append(runOptions.WorkingDirectory);
        }
        else
        {
            fileNameBuilder.Append(_configurationsPath);
            fileNameBuilder.Append(Path.DirectorySeparatorChar);

            if (!string.IsNullOrWhiteSpace(pluginName))
            {
                fileNameBuilder.Append(pluginName);
                fileNameBuilder.Append(Path.DirectorySeparatorChar);
            }
        }

        fileNameBuilder.Append(GetConfigurationName(configurationType));

        if (!string.IsNullOrWhiteSpace(key))
        {
            fileNameBuilder.Append(Path.DirectorySeparatorChar);
            fileNameBuilder.Append(key);
        }

        fileNameBuilder.Append(".toml");

        return fileNameBuilder.ToString();
    }

    private static string GetConfigurationName(Type configurationType)
    {
        return configurationType.GetCustomAttribute<ConfigurationAttribute>() switch
        {
            { Name: { } name } when !string.IsNullOrWhiteSpace(name) => name,
            _ => configurationType.Name
        };
    }

    private static bool IsRootConfiguration(Type configurationType)
    {
        return configurationType.GetCustomAttribute<RootConfigurationAttribute>() is not null;
    }

    private string? GetPluginNameFromConfiguration(Type configurationType)
    {
        plugins.TryGetPluginFromType(configurationType, out var plugin);
        return plugin?.Name;
    }

    private void EnsureConfigurationsPathExists()
    {
        EnsureConfigurationsPathExists(_configurationsPath);
    }

    private void EnsureConfigurationsPathExists(string path)
    {
        if (Directory.Exists(path))
            return;

        logger.LogTrace("Creating {Path} directory", path);
        Directory.CreateDirectory(path);
    }

    private static async ValueTask WaitFileLockAsync(string filePath, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!File.Exists(filePath))
                    break;

                using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                break;
            }
            catch (IOException)
            {
                if (!File.Exists(filePath))
                    throw;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static void SwapConfiguration(object configuration1, object configuration2)
    {
        var configurationType1 = configuration1.GetType();
        var configurationType2 = configuration2.GetType();

        var fields1 = configurationType1.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        var fields2 = configurationType2.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        if (fields1.Length != fields2.Length)
            throw new InvalidOperationException($"Cannot swap configurations: {configurationType1} and {configurationType2} have different number of fields");

        for (var i = 0; i < fields1.Length; i++)
        {
            var field1 = fields1[i];
            var field2 = fields2[i];

            if (field1.Name != field2.Name)
                throw new InvalidOperationException($"Cannot swap configurations: {configurationType1} and {configurationType2} have different field names");

            if (field1.FieldType != field2.FieldType)
                throw new InvalidOperationException($"Cannot swap configurations: {configurationType1} and {configurationType2} have different field types");

            var field1Value = field1.GetValue(configuration1);
            var field2Value = field2.GetValue(configuration2);

            if (field1Value is null || field2Value is null || IsSystemType(field1Value) || IsSystemType(field2Value))
            {
                field1.SetValue(configuration1, field2Value);
                field2.SetValue(configuration2, field1Value);
            }
            else
            {
                SwapConfiguration(field1Value, field2Value);
            }
        }

        static bool IsSystemType(object value) => value.GetType().FullName?.StartsWith("System") ?? true;
    }
}
