# Builder stage: compile portablemc from source
FROM rust:1.98-slim-trixie@sha256:f47a8de237dcbb0b0ce1099901e60a89728e3d51f24e664b40e947171538ade7 AS builder

RUN apt-get update && apt-get install -y --no-install-recommends \
    pkg-config \
    libssl-dev \
 && rm -rf /var/lib/apt/lists/*

RUN cargo install portablemc-cli --root /usr/local

# Debian 12 still packages the LWJGL 2 JNI libraries used by legacy clients.
FROM debian:bookworm-slim@sha256:3783cc01769c7b2b1b83a5c5ad96c815348e28ed7da68e2e3687004faa906251 AS legacy-natives

WORKDIR /native-packages
RUN apt-get update && apt-get download liblwjgl-java-jni libjinput-jni

# Runtime stage
FROM debian:13@sha256:9cc080028c43b27d2074d63a5f9caf7166d731494965616c1a6d2827a004585c AS runtime

ARG TARGETARCH

ENV DEBIAN_FRONTEND=noninteractive
ENV LIBGL_ALWAYS_SOFTWARE=1
# SDL-based clients request an sRGB framebuffer that Xvfb does not expose through GLX.
ENV SDL_VIDEO_FORCE_EGL=1

# Copy pre-built portablemc binary from builder
COPY --from=builder /usr/local/bin/portablemc /usr/local/bin/portablemc
COPY --from=legacy-natives /native-packages/ /tmp/legacy-natives/

# Install only runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    jq \
    imagemagick \
    jattach \
    libasound2 \
    libegl-mesa0 \
    libegl1 \
    libflite1 \
    libfontconfig1 \
    libfreetype6 \
    libgl1-mesa-dri \
    libicu-dev \
    libopenal1 \
    libxcursor1 \
    libxi6 \
    libxrandr2 \
    libxtst6 \
    matchbox-window-manager \
    nginx \
    novnc \
    procps \
    python3 \
    tk \
    tini \
    util-linux \
    websockify \
    x11-utils \
    x11-xserver-utils \
    x11vnc \
    xdotool \
    xxd \
    xvfb \
    xfwm4 \
    /tmp/legacy-natives/*.deb \
 && rm -rf /var/lib/apt/lists/* /tmp/legacy-natives

# Mojang does not publish ARM64 JVMs for every Java version required by older clients.
RUN set -e; if [ "$TARGETARCH" = "arm64" ]; then \
      mkdir -p /opt/portableminecraftclient; \
      dpkg --add-architecture amd64; \
      apt-get update; \
      apt-get install -y --no-install-recommends \
        libc6:amd64 \
        libasound2:amd64 \
        libfontconfig1:amd64 \
        libfreetype6:amd64 \
        libgl1:amd64 \
        libgl1-mesa-dri:amd64 \
        libstdc++6:amd64 \
        libxcursor1:amd64 \
        libxi6:amd64 \
        libxrandr2:amd64 \
        libxtst6:amd64 \
        qemu-user; \
      rm -rf /var/lib/apt/lists/*; \
      for javaVersion in 8 16 17 21 25; do \
        downloadUrl="$(curl --fail --silent --get 'https://api.azul.com/metadata/v1/zulu/packages/' \
          --data-urlencode "java_version=$javaVersion" \
          --data-urlencode 'os=linux' \
          --data-urlencode 'arch=arm' \
          --data-urlencode 'archive_type=tar.gz' \
          --data-urlencode 'java_package_type=jre' \
          --data-urlencode 'release_status=ga' \
          --data-urlencode 'availability_types=CA' \
          | jq -r '[.[] | select((.name | contains("-linux_aarch64.tar.gz")) and (.name | contains("-fx-") | not))][0].download_url // empty')"; \
        test -n "$downloadUrl"; \
        mkdir -p "/usr/lib/jvm/zulu-$javaVersion"; \
        curl --fail --location --silent --show-error "$downloadUrl" \
          | tar --extract --gzip --directory "/usr/lib/jvm/zulu-$javaVersion" --strip-components=1; \
      done; \
      mkdir -p /opt/zulu-8-x86_64; \
      curl --fail --location --silent --show-error \
        https://cdn.azul.com/zulu/bin/zulu8.96.0.19-ca-jre8.0.502-linux_x64.tar.gz \
        | tar --extract --gzip --directory /opt/zulu-8-x86_64 --strip-components=1; \
      # LWJGL 2.9.0 and 2.9.1 cannot be mapped through x86_64 QEMU on 16 KiB-page ARM64 hosts. \
      # Bundle the ABI-compatible 2.9.4 native so the Java wrapper can replace them before launch. \
      legacyLwjglNativeArchive=/tmp/lwjgl-platform-2.9.4-natives-linux.jar; \
      curl --fail --location --silent --show-error --retry 5 --retry-all-errors --connect-timeout 15 --max-time 300 \
        https://libraries.minecraft.net/org/lwjgl/lwjgl/lwjgl-platform/2.9.4-nightly-20150209/lwjgl-platform-2.9.4-nightly-20150209-natives-linux.jar \
        --output "$legacyLwjglNativeArchive"; \
      echo "931074f46c795d2f7b30ed6395df5715cfd7675b  $legacyLwjglNativeArchive" | sha1sum --check --status; \
      python3 -c 'import sys, zipfile; archive, destination = sys.argv[1:]; open(destination, "wb").write(zipfile.ZipFile(archive).read("liblwjgl64.so"))' \
        "$legacyLwjglNativeArchive" /opt/portableminecraftclient/liblwjgl64.so; \
      rm "$legacyLwjglNativeArchive"; \
      for lwjglVersion in 3.3.3 3.4.1; do \
        for lwjglModule in lwjgl lwjgl-freetype lwjgl-glfw lwjgl-jemalloc lwjgl-openal lwjgl-opengl lwjgl-stb lwjgl-tinyfd; do \
          lwjglLibraryDirectory="/root/.minecraft/libraries/org/lwjgl/$lwjglModule/$lwjglVersion"; \
          mkdir -p "$lwjglLibraryDirectory"; \
          for lwjglClassifier in '' '-natives-linux-arm64'; do \
            lwjglLibraryFileName="$lwjglModule-$lwjglVersion$lwjglClassifier.jar"; \
            curl --fail --location --silent --show-error --retry 5 --retry-all-errors --connect-timeout 15 --max-time 300 \
              "https://repo.maven.apache.org/maven2/org/lwjgl/$lwjglModule/$lwjglVersion/$lwjglLibraryFileName" \
              --output "$lwjglLibraryDirectory/$lwjglLibraryFileName"; \
          done; \
        done; \
      done; \
      curl --fail --location --silent --show-error --retry 5 --retry-all-errors --connect-timeout 15 --max-time 300 \
        https://libraries.minecraft.net/org/lwjgl/lwjgl/3.4.1/lwjgl-3.4.1-unsafe.jar \
        --output /opt/portableminecraftclient/lwjgl-3.4.1-unsafe.jar; \
      cp /root/.minecraft/libraries/org/lwjgl/lwjgl/3.4.1/lwjgl-3.4.1-natives-linux-arm64.jar \
        /opt/portableminecraftclient/lwjgl-3.4.1-natives-linux-arm64.jar; \
    fi

# Install only the ASP.NET Core 10 runtime required by the pre-published API.
RUN curl --fail --location --silent --show-error --retry 5 --retry-all-errors https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="${DOTNET_ROOT}:${PATH}"

# Fix sound errors by using the null audio driver
RUN printf "pcm.!default { type null }\nctl.!default { type null }\n" > /etc/asound.conf

# Skip Minecraft accessibility screen
RUN mkdir -p "$HOME/.minecraft" && touch "$HOME/.minecraft/options.txt"

WORKDIR /app

COPY --chmod=0755 list-minecraft-release-versions portablemc-dry-run-with-retries /usr/local/bin/
RUN ln -s /usr/share/novnc/vnc.html /usr/share/novnc/index.html

EXPOSE 80
ENV ASPNETCORE_URLS=http://127.0.0.1:8081
ENV CURSEFORGE_API_BASE_URL=https://api.curseforge.com

HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 CMD curl --fail --silent --output /dev/null http://127.0.0.1/api/health || exit 1
ENTRYPOINT ["tini", "--", "portableminecraftclient-entrypoint"]

FROM runtime AS offline-assets

ARG FIRST_RELEASE_VERSION
ARG LAST_RELEASE_VERSION
# Invalidates the asset layer when its source inputs or Minecraft release set change.
ARG OFFLINE_ASSETS_REVISION=local

# Dry-run all release versions in reverse order (from oldest to latest).
# The downloaded content is deterministic, so a rebuild with rewritten timestamps reproduces the same
# layer digest and pulls skip it. The portablemc manifest cache changes over time, so it is kept out of this layer.
RUN test -n "$FIRST_RELEASE_VERSION" \
 && test -n "$OFFLINE_ASSETS_REVISION" \
 && list-minecraft-release-versions "$FIRST_RELEASE_VERSION" "$LAST_RELEASE_VERSION" | while IFS= read -r minecraftVersion; do portablemc-dry-run-with-retries "mojang:$minecraftVersion"; done \
 && rm -rf /root/.cache/portablemc-cache /tmp/hsperfdata_root

# portablemc cannot resolve versions without network access unless the manifest cache exists.
RUN portablemc-dry-run-with-retries "mojang:$(list-minecraft-release-versions "$FIRST_RELEASE_VERSION" "$LAST_RELEASE_VERSION" | tail -n 1)"

FROM runtime AS version-assets

ARG MINECRAFT_VERSION
RUN test -n "$MINECRAFT_VERSION"
RUN portablemc-dry-run-with-retries "mojang:$MINECRAFT_VERSION"

# Publish the API once during the image build. Runtime startup never restores or compiles source code.
FROM debian:13@sha256:9cc080028c43b27d2074d63a5f9caf7166d731494965616c1a6d2827a004585c AS dotnet-builder

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl default-jdk-headless libicu-dev maven \
 && rm -rf /var/lib/apt/lists/* \
 && curl --fail --location --silent --show-error --retry 5 --retry-all-errors https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && chmod +x /tmp/dotnet-install.sh \
 && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && rm /tmp/dotnet-install.sh

ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="${DOTNET_ROOT}:${PATH}"

WORKDIR /source
COPY . .
RUN mvn --batch-mode --file Agent/pom.xml package
RUN dotnet publish PortableMinecraftClient.csproj --configuration Release --output /publish /p:UseAppHost=false

FROM scratch AS application-files

COPY --from=dotnet-builder /publish/ /app/
COPY --from=dotnet-builder /source/Agent/target/void-client-agent.jar /opt/portableminecraftclient/void-client-agent.jar
COPY --chmod=0755 entrypoint /usr/local/bin/portableminecraftclient-entrypoint
COPY --chmod=0755 launch-portableminecraftclient /usr/local/bin/launch-portableminecraftclient
COPY --chmod=0755 java-x86_64 /usr/local/bin/java-x86_64
COPY --chmod=0755 java-arm64-lwjgl2 /usr/local/bin/java-arm64-lwjgl2
COPY nginx.conf /etc/nginx/nginx.conf
COPY options.txt sodium-options.json splash.tcl /opt/portableminecraftclient/

FROM runtime AS latest

COPY --from=application-files / /

FROM offline-assets AS offline

COPY --link --from=application-files / /

FROM version-assets AS version

COPY --from=application-files / /
