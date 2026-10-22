using System.Collections.Concurrent;
using System.Reflection;
using DryIoc;
using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Plugins;
using Void.Proxy.Api.Events.Services;
using Void.Proxy.Api.Players;
using Void.Proxy.Api.Players.Contexts;
using Void.Proxy.Api.Plugins;
using Void.Proxy.Api.Plugins.Dependencies;
using Void.Proxy.Plugins.Dependencies.Extensions;

namespace Void.Proxy.Plugins.Dependencies;

public class DependencyService(ILogger<DependencyService> logger, IContainer rootContainer) : IDependencyService
{
    private readonly ConcurrentDictionary<Assembly, PluginContainer> _plugins = [];

    public TService CreateInstance<TService>(CancellationToken cancellationToken = default, params object[] parameters)
    {
        return CreateInstance<TService>(typeof(TService), cancellationToken, parameters);
    }

    public TService CreateInstance<TService>(Type serviceType, CancellationToken cancellationToken = default, params object[] parameters)
    {
        return (TService?)CreateInstance(serviceType, cancellationToken, parameters) ??
               throw new InvalidOperationException($"Unable to cast instance of {serviceType} to {typeof(TService)}");
    }

    public object CreateInstance(Type serviceType, CancellationToken cancellationToken = default, params object[] parameters)
    {
        var assembly = serviceType.Assembly;

        if (serviceType.IsAssignableTo(typeof(IPlugin)))
        {
            logger.LogTrace("Injecting {PluginType}", serviceType);

            var pluginContainers = GetPluginContainer(assembly);

            // Plugin => Plugin
            pluginContainers.Root.Add(ServiceDescriptor.Singleton(serviceType, serviceType));
            pluginContainers.Root.Add(ServiceDescriptor.Singleton(provider => (IPlugin)provider.GetRequiredService(serviceType)));
            pluginContainers.Root.Add(ServiceDescriptor.Singleton(provider => GetEntryPoint(assembly).GetRequiredService<ILoggerFactory>().CreateLogger(serviceType.Name)));

            GetEntryPoint(preferredAssembly: assembly).GetRequiredService(serviceType);
        }

        var composite = GetEntryPoint(preferredAssembly: assembly);
        var instance = composite.GetService(serviceType) ?? ActivatorUtilities.CreateInstance(composite, serviceType, parameters);

        if (instance is not IEventListener listener)
            return instance;

        var events = rootContainer.Resolve<IEventService>();
        events.RegisterListeners(cancellationToken, listener);

        return instance;
    }

    public void Register(Action<ServiceCollection> configure, bool activate = true)
    {
        var services = new ServiceCollection();
        configure(services);

        var assembly = configure.Method.DeclaringType?.Assembly ?? GetType().Assembly;

        foreach (var service in services)
            GetPluginContainer(assembly).Root.Add(service);

        if (!activate)
            return;

        foreach (var service in services)
        {
            if (service.Lifetime is ServiceLifetime.Transient)
                continue;

            var serviceType = service.ServiceType;

            if (serviceType.IsOpenGeneric())
                continue;

            switch (service.Lifetime)
            {
                case ServiceLifetime.Scoped:
                    {
                        var players = rootContainer.GetRequiredService<IPlayerService>();

                        foreach (var player in players.All)
                            GetEntryPoint(player, assembly).GetRequiredService(serviceType);

                        break;
                    }
                case ServiceLifetime.Singleton:
                    GetEntryPoint(assembly).GetRequiredService(serviceType);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported service lifetime: {service.Lifetime}");
            }
        }
    }

    public object? GetService(Type serviceType)
    {
        return GetEntryPoint(preferredAssembly: serviceType.Assembly).GetService(serviceType);
    }

    public TService? GetService<TService>()
    {
        return GetEntryPoint(preferredAssembly: typeof(TService).Assembly).GetService<TService>();
    }

    public void ActivatePlayerScope(IPlayerContext context)
    {
        var playerStableHashCode = context.Player.GetStableHashCode();
        var assemblies = new Queue<Assembly>(_plugins.Keys);

        while (assemblies.TryDequeue(out var assembly))
        {
            var playerScope = GetPlayerScope(assembly, playerStableHashCode, context);

            foreach (var registration in playerScope.Container.GetServiceRegistrations())
            {
                if (registration.ServiceType.IsOpenGeneric())
                    continue;

                if (registration.Factory?.Reuse is not CurrentScopeReuse)
                    continue;

                _ = GetEntryPoint(context.Player, assembly).GetRequiredService(registration.ServiceType);
            }
        }
    }

    public void DisposePlayerScope(IPlayerContext context)
    {
        var events = rootContainer.Resolve<IEventService>();
        var playerStableHashCode = context.Player.GetStableHashCode();

        foreach (var plugin in _plugins.Values)
        {
            if (!plugin.Scopes.TryRemove(playerStableHashCode, out var resolverContext))
                continue;

            foreach (var scope in resolverContext.CurrentScope)
            {
                foreach (var service in scope.GetSnapshotOfServicesWithFactoryIDs())
                {
                    if (service.Value is not IEventListener listener)
                        continue;

                    events.UnregisterListeners(listener);
                }
            }

            resolverContext.Dispose();
        }
    }

    public bool TryGetServiceReuse(Type serviceType, out ServiceLifetime reuse)
    {
        var pluginContainer = GetPluginContainer(serviceType.Assembly);
        var container = pluginContainer.Root.Container;
        var registration = container.GetServiceRegistrations().FirstOrDefault(registration => registration.ServiceType == serviceType);
        var registered = registration.Factory is not null;

        reuse = registration.Factory?.Reuse switch
        {
            SingletonReuse => ServiceLifetime.Singleton,
            CurrentScopeReuse => ServiceLifetime.Scoped,
            _ => ServiceLifetime.Transient
        };

        return registered;
    }

    public IServiceProvider GetEntryPoint(IPlayer player, Assembly? preferredAssembly = null)
    {
        var playerStableHashCode = player.GetStableHashCode();

        foreach (var assembly in _plugins.Keys)
            GetPlayerScope(assembly, playerStableHashCode, player.Context);

        return CombineLazy(
            () => GetScopedContainers(playerStableHashCode, preferredAssembly),
            () => GetRootContainers(preferredAssembly));
    }

    public IServiceProvider GetEntryPoint(Assembly? preferredAssembly = null)
    {
        return CombineLazy(() => GetRootContainers(preferredAssembly));
    }

    public bool IsInPlayerScope(IPlayer player, object service)
    {
        var serviceType = service.GetType();
        var playerStableHashCode = player.GetStableHashCode();

        if (!_plugins.TryGetValue(serviceType.Assembly, out var pluginContainer))
            return false;

        if (!pluginContainer.Scopes.TryGetValue(playerStableHashCode, out var playerScope))
            return false;

        return playerScope.CurrentScope.SelectMany(scope => scope.GetSnapshotOfServicesWithFactoryIDs()).Any(scopedService => ReferenceEquals(scopedService.Value, service));
    }

    [Subscribe(PostOrder.First)]
    public void OnPluginUnloading(PluginUnloadingEvent @event)
    {
        var assembly = @event.Plugin.GetType().Assembly;

        var events = rootContainer.GetRequiredService<IEventService>();
        events.UnregisterListeners(events.Listeners.Where(listener => listener.GetType().Assembly == assembly));

        if (!_plugins.TryRemove(assembly, out var container))
            return;

        container.Root.Container.Dispose();

        foreach (var scope in container.Scopes.Values)
            scope.Dispose();
    }

    private IEnumerable<IServiceProvider> GetScopedContainers(int playerStableHashCode, Assembly? preferredAssembly = null)
    {
        foreach (var assembly in GetPluginAssemblies(preferredAssembly))
        {
            if (!_plugins.TryGetValue(assembly, out var pluginContainer))
                continue;

            if (!pluginContainer.Scopes.TryGetValue(playerStableHashCode, out var playerScope))
                continue;

            yield return playerScope;
        }

        foreach (var serviceProvider in GetRootContainers(preferredAssembly))
            yield return serviceProvider;
    }

    private IEnumerable<IServiceProvider> GetRootContainers(Assembly? preferredAssembly = null)
    {
        if (preferredAssembly is not null && _plugins.ContainsKey(preferredAssembly))
            yield return GetPluginContainer(preferredAssembly).Root;

        yield return rootContainer;

        foreach (var assembly in GetPluginAssemblies(preferredAssembly))
        {
            if (assembly == preferredAssembly)
                continue;

            yield return GetPluginContainer(assembly).Root;
        }
    }

    private IEnumerable<Assembly> GetPluginAssemblies(Assembly? preferredAssembly = null)
    {
        var assemblies = _plugins.Keys.AsEnumerable();

        if (preferredAssembly is not null)
            assemblies = assemblies.OrderByDescending(assembly => assembly == preferredAssembly);

        return assemblies;
    }

    private IServiceProvider GetPlayerScope(Assembly assembly, int playerStableHashCode, IPlayerContext context)
    {
        var container = GetPluginContainer(assembly);

        if (container.Scopes.TryGetValue(playerStableHashCode, out var playerScope))
            return playerScope;

        playerScope = container.Root.Container.OpenScope(nameof(IPlayer));
        container.Scopes[playerStableHashCode] = playerScope;

        playerScope.Use(context);

        return playerScope;
    }

    private PluginContainer GetPluginContainer(Assembly pluginAssembly)
    {
        if (_plugins.TryGetValue(pluginAssembly, out var pluginContainer))
            return pluginContainer;

        var emptyContainer = Combine();
        pluginContainer = new PluginContainer(Root: emptyContainer, Scopes: []);

        if (_plugins.TryAdd(pluginAssembly, pluginContainer))
            return pluginContainer;

        emptyContainer.Dispose();

        if (_plugins.TryGetValue(pluginAssembly, out pluginContainer))
            return pluginContainer;

        throw new InvalidOperationException($"Failed to get or create plugin container for {pluginAssembly.FullName}.");
    }

    private Container Combine(params IServiceProvider[] containers)
    {
        return CombineLazy(() => containers);
    }

    private Container CombineLazy(Func<IEnumerable<IServiceProvider>> getContainers, Func<IEnumerable<IServiceProvider>>? getSingletonContainers = null)
    {
        var events = rootContainer.Resolve<IEventService>();
        var getSingletonResolutionContainers = getSingletonContainers ?? getContainers;

        return new Container(rootContainer.Rules.WithUnknownServiceResolvers(ResolveFactory));

        InstanceFactory? ResolveFactory(Request request)
        {
            var getResolutionContainers =
                request.Flags.HasFlag(RequestFlags.IsSingletonOrDependencyOfSingleton) || IsSingletonService(request.ServiceType)
                    ? getSingletonResolutionContainers
                    : getContainers;

            var service = ResolveService(request.ServiceType, getResolutionContainers, request.Container);
            return service is null ? null : InstanceFactory.Of(service);
        }

        object? ResolveService(Type serviceType, Func<IEnumerable<IServiceProvider>> getServiceProviders, IContainer entryPointContainer)
        {
            var serviceProviders = getServiceProviders()
                .OrderByDescending(serviceProvider => GetServiceProviderPriority(serviceProvider.Container, serviceType));

            foreach (var serviceProvider in serviceProviders)
            {
                var container = serviceProvider.Container;

                if (!container.CanGetService(serviceType))
                    continue;

                var configuredContainer = container.With(
                    parent: entryPointContainer,
                    rules: container.Rules.WithUnknownServiceResolvers(ResolveFactory),
                    scopeContext: null,
                    registrySharing: RegistrySharing.CloneAndDropCache,
                    singletonScope: container.SingletonScope,
                    currentScope: null,
                    isRegistryChangePermitted: null);

                var resolver = serviceProvider is IResolverContext { CurrentScope: not null } resolverContext
                    ? configuredContainer.WithCurrentScope(resolverContext.CurrentScope)
                    : configuredContainer;

                var service = resolver.Resolve(serviceType, IfUnresolved.ReturnDefault);

                if (service is null)
                    continue;

                if (service is IEventListener listener)
                    events.RegisterListeners(listener);

                return service;
            }

            return null;
        }

        static int GetServiceProviderPriority(IContainer container, Type serviceType)
        {
            if (!serviceType.IsConstructedGenericType)
                return 0;

            if (container.IsRegistered(serviceType))
                return 3;

            if (HasRelatedClosedGenericRegistration(container, serviceType))
                return 2;

            if (container.IsRegistered(serviceType.GetGenericTypeDefinition()))
                return 1;

            return 0;
        }

        static bool HasRelatedClosedGenericRegistration(IContainer container, Type serviceType)
        {
            var genericArguments = serviceType.GetGenericArguments();

            return container
                .GetServiceRegistrations()
                .Any(registration =>
                {
                    if (!registration.ServiceType.IsConstructedGenericType)
                        return false;

                    return registration.ServiceType.GetGenericArguments().Any(genericArguments.Contains);
                });
        }

        bool IsSingletonService(Type serviceType)
        {
            return getSingletonResolutionContainers()
                .Select(serviceProvider => serviceProvider.Container)
                .SelectMany(container => container.GetServiceRegistrations())
                .Any(registration => Matches(serviceType, registration.ServiceType) && registration.Factory?.Reuse is SingletonReuse);
        }

        static bool Matches(Type requestedServiceType, Type registeredServiceType)
        {
            return requestedServiceType == registeredServiceType || requestedServiceType.IsConstructedGenericType && requestedServiceType.GetGenericTypeDefinition() == registeredServiceType;
        }
    }

    // Players join and leave on threads of their own, each opening or disposing its scopes.
    private record PluginContainer(IServiceProvider Root, ConcurrentDictionary<int, IResolverContext> Scopes);
}
