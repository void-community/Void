using Void.Proxy.Api.Events;
using Void.Proxy.Api.Events.Network;
using Void.Proxy.Api.Network.Messages;
using Void.Proxy.Plugins.Common.Extensions;

namespace Void.Proxy.Plugins.Common.Services.Bundles;

public abstract class AbstractBundleService : IPluginCommonService
{
    [Subscribe(PostOrder.First)]
    public void OnMessageSent(MessageSentEvent @event)
    {
        if (!IsBundlePacket(@event.Message))
            return;

        var bundles = @event.Player.GetBundles(GetType().Assembly);
        bundles.ToggleBundle();
    }

    protected abstract bool IsBundlePacket(INetworkMessage message);
}
