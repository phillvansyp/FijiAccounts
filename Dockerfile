# syntax=docker/dockerfile:1

FROM alpine:3.22
RUN apk add --no-cache \
    ca-certificates \
    font-dejavu \
    icu-libs \
    libgcc \
    libssl3 \
    libstdc++ \
    tzdata \
    zlib
WORKDIR /app
COPY artifacts/publish/ .
RUN addgroup --system --gid 1654 app \
    && adduser --system --uid 1654 --ingroup app app \
    && mkdir -p /app/data /app/keys \
    && chown -R app:app /app
USER app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENTRYPOINT ["./FijiAccounts.Web"]
