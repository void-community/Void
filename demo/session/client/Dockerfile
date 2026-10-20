FROM ghcr.io/void-community/portable-minecraft-client:latest

COPY sodium-options.json /opt/void-demo/sodium-options.json
COPY prepare /etc/portableminecraftclient/prepare.d/void-demo
# The demo's server list has one entry, "Void Proxy" at void:25565, the session's proxy.
RUN printf '%s' '0a0000090007736572766572730a0000000101000668696464656e000800026970000a766f69643a32353536350800046e616d65000a566f69642050726f78790000' | xxd -r -p > /opt/void-demo/servers.dat \
 && sed -i 's/\r$//' /etc/portableminecraftclient/prepare.d/void-demo \
 && chmod 0755 /etc/portableminecraftclient/prepare.d/void-demo
