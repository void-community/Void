# Builder stage: compile portablemc from source
FROM rust:1.98-slim-trixie@sha256:17d1ba895198f9934c6314ec5346a0d5115372f3243390c3d731e242f35c2f27 AS builder

RUN apt-get update && apt-get install -y --no-install-recommends \
    pkg-config \
    libssl-dev \
 && rm -rf /var/lib/apt/lists/*

RUN cargo install portablemc-cli --root /usr/local

# Runtime stage
FROM debian:13@sha256:f324c7ff54321e8d9c588493a20244965938ce0aa50bbd1022d38010e9ffc4b1 AS runtime

ARG TARGETARCH

ENV DEBIAN_FRONTEND=noninteractive
ENV LIBGL_ALWAYS_SOFTWARE=1

# Copy pre-built portablemc binary from builder
COPY --from=builder /usr/local/bin/portablemc /usr/local/bin/portablemc

# Install only runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    jq \
    imagemagick \
    jattach \
    libasound2 \
    libflite1 \
    libfontconfig1 \
    libfreetype6 \
    libgl1-mesa-dri \
    libicu-dev \
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
 && rm -rf /var/lib/apt/lists/*

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

ARG FIRST_RELEASE_VERSION=1.7.2

# Dry-run all release versions in reverse order (from oldest to latest)
RUN list-minecraft-release-versions "$FIRST_RELEASE_VERSION" | while IFS= read -r minecraftVersion; do portablemc-dry-run-with-retries "mojang:$minecraftVersion"; done

FROM runtime AS version-assets

ARG MINECRAFT_VERSION
RUN test -n "$MINECRAFT_VERSION"
RUN portablemc-dry-run-with-retries "mojang:$MINECRAFT_VERSION"

# Publish the API once during the image build. Runtime startup never restores or compiles source code.
FROM debian:13@sha256:f324c7ff54321e8d9c588493a20244965938ce0aa50bbd1022d38010e9ffc4b1 AS dotnet-builder

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
COPY nginx.conf /etc/nginx/nginx.conf
COPY options.txt sodium-options.json splash.tcl /opt/portableminecraftclient/

FROM runtime AS latest

COPY --from=application-files / /

FROM offline-assets AS offline

COPY --from=application-files / /

FROM version-assets AS version

COPY --from=application-files / /
