# Online
[**void-demo.caunt.world**](https://void-demo.caunt.world/)

⚠️ **Warning!**  

You're not supposed to run commands below unless you want to run whole Void demo website.  
If you're looking for a examples of how to run Void proxy locally, follow [**this guide**](https://void.caunt.world/docs/).

# Local
Run these commands from the `demo` directory.

## Build
`docker build -t caunt/void-demo:latest .`

## Run
`docker run --name void-demo --rm --privileged -v demo-dind:/var/lib/docker -p 8080:80 -e REDIRECT_LOGS=true caunt/void-demo:latest`  
→ [**localhost:8080**](http://localhost:8080/)

## Cleanup
`docker rm -f void-demo && docker volume rm demo-dind && docker rmi caunt/void-demo:latest`

## Testing
`docker volume rm demo-dind || true && docker rm -f void-demo || true && docker build -t caunt/void-demo:latest . && docker run --name void-demo --rm --privileged -v demo-dind:/var/lib/docker -p 8080:80 -e REDIRECT_LOGS=true caunt/void-demo:latest`
→ [**localhost:8080**](http://localhost:8080/)

## Publish
- `docker buildx create --name multiarch --driver docker-container --use && docker buildx inspect --bootstrap`
- `docker buildx build --platform linux/amd64,linux/arm64 -t caunt/void-demo:latest --push .`

# Production
Pull the published image and run it in the background behind a reverse proxy or tunnel:

`docker pull caunt/void-demo:latest && docker run -d --name void-demo --restart unless-stopped --privileged -v demo-dind:/var/lib/docker -p 127.0.0.1:8080:80 -e REDIRECT_LOGS=true caunt/void-demo:latest`

## Automatic updates
The controller refreshes everything sessions depend on at startup and then every `REFRESH_INTERVAL_SECONDS`, so new Minecraft releases need no restart or redeploy:
- Pulls `ghcr.io/void-community/portable-minecraft-client:latest` and rebuilds the shared session images when it or `caunt/void:dev` changes, including the demo's client image, which adds Sodium and the server list. New sessions use them; running sessions are not touched.
- Recreates only the shared Paper server when a newer Paper build or ViaVersion, ViaBackwards or ViaRewind release is available. Updates wait until no session is active. A new Minecraft version or plugin release is forced after `BACKEND_RECREATE_GRACE_SECONDS`, ending active sessions; a build-only Paper update always waits.
- Asks each client for the newest Minecraft release with a stable NeoForge build that the Paper server and ViaVersion accept.

| Variable | Default | Meaning |
| --- | --- | --- |
| `REFRESH_INTERVAL_SECONDS` | `600` | Delay between refresh passes |
| `BACKEND_RECREATE_GRACE_SECONDS` | `3600` | How long a Minecraft or plugin update waits for active sessions before ending them |
| `SESSION_START_TIMEOUT_SECONDS` | `1200` | Time a session may take to start before it is shown as failed |
| `SESSION_TTL_SECONDS` | `7200` | Lifetime of a started session |
