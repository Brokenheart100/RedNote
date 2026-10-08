FROM zhenghaoz/gorse-in-one:0.5.11
RUN apt-get update && apt-get install -y --no-install-recommends jq && rm -rf /var/lib/apt/lists/*
COPY config.toml start.sh /etc/gorse/
ENTRYPOINT ["/bin/sh", "/etc/gorse/start.sh"]
