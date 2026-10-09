# Community MinIO no longer publishes the Docker Hub image. Build the official release source.
FROM golang:1.27-bookworm AS build
ARG MINIO_RELEASE=RELEASE.2025-10-15T17-29-55Z
RUN git clone --depth 1 --branch "${MINIO_RELEASE}" https://github.com/minio/minio.git /src
WORKDIR /src
RUN --mount=type=cache,target=/go/pkg/mod --mount=type=cache,target=/root/.cache/go-build \
    CGO_ENABLED=0 go build -tags kqueue -trimpath \
    -ldflags="$(MINIO_RELEASE=RELEASE go run buildscripts/gen-ldflags.go)" -o /out/minio .

FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/minio /usr/local/bin/minio
COPY --from=build /src/LICENSE /usr/share/doc/minio/LICENSE
EXPOSE 9000 9001
ENTRYPOINT ["minio"]
