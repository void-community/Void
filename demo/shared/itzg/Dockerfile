FROM tsl0922/ttyd@sha256:974d056ecba8d000395a5971123640adf58b3bb300f643ff2abba190a0c19637 AS ttyd_source

FROM debian:bookworm-slim@sha256:88200866dfff7ea7f5cbcb6ec7c8a701889efe6fe859fe64d6990e4b07ea4171 AS tmux_source
RUN apt-get update && apt-get install -y --no-install-recommends tmux && rm -rf /var/lib/apt/lists/*
RUN set -e; \
  mkdir -p /out/usr/local/tmux/bin /out/usr/local/tmux/lib; \
  cp /usr/bin/tmux /out/usr/local/tmux/bin/; \
  ldd /usr/bin/tmux | awk '$3 ~ /^\// {print $3}' | grep -vE '/ld-linux|/libc\.so|/libm\.so|/libpthread\.so|/librt\.so|/libdl\.so|/libresolv\.so|/libnsl\.so|/libutil\.so' | while read -r libraryPath; do cp "$libraryPath" /out/usr/local/tmux/lib/; done

FROM itzg/minecraft-server@sha256:77280d10744fd0482b7c510cd6b7ba7a5a609be5ef91cd593374e7df36de826b

COPY --from=ttyd_source /usr/bin/ttyd /usr/local/bin/ttyd
COPY --from=tmux_source /out/usr/local/tmux /usr/local/tmux

COPY <<'EOF' /entrypoint.sh
#!/bin/sh
set -e

tmux() {
  env LD_LIBRARY_PATH=/usr/local/tmux/lib /usr/local/tmux/bin/tmux "$@"
}

# Create the session (detached)
tmux new-session -d -s main -n shell sh -lc 'unset LD_LIBRARY_PATH; exec /start' || true

# Set options
tmux set-option -g -w aggressive-resize off
tmux set-option -g -w window-size largest

# Start ttyd
exec /usr/local/bin/ttyd -R -p 80 env LD_LIBRARY_PATH=/usr/local/tmux/lib /usr/local/tmux/bin/tmux attach -t main
EOF

RUN sed -i 's/\r$//' /entrypoint.sh && chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
