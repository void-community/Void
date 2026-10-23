namespace Void.Proxy.Plugins.ModsSupport.SlowJoin;

public class Settings
{
    public bool Enabled { get; set; } = true;

    public int FirstKeepAliveTimeout { get; set; } = 180;

    public int PromptAnswers { get; set; } = 8;

    public int PromptAnswerTime { get; set; } = 5;
}
