# Frontend image. Stages:
#
#   deps    node:22-alpine + the locked dependencies (npm ci)
#   test    runs the whole test suite; build it on its own to run the tests with nothing
#           but Docker:   docker build --target test ./frontend
#   build   npm run build -> /app/dist
#   (last)  nginx serving dist/ on port 80; this is the stage `docker compose up` builds,
#           so starting the stack does not run the tests
#
# The backend URL is NOT baked in. At start-up the entrypoint writes /config.js from the
# API_BASE_URL environment variable, so one image works against any backend.
#
# Node 22.22.2 or newer is what jsdom needs; node:22-alpine is above that.

FROM node:22-alpine AS deps
WORKDIR /app
# A slow or flaky connection to the npm registry should cost time, not fail the build:
# generous timeouts, a few retries, and a cache mount so a retried build reuses what it
# already downloaded (needs BuildKit, the default builder since Docker Engine 23).
ENV NPM_CONFIG_FETCH_RETRIES=5 \
    NPM_CONFIG_MAXSOCKETS=6 \
    NPM_CONFIG_FETCH_RETRY_MINTIMEOUT=10000 \
    NPM_CONFIG_FETCH_RETRY_MAXTIMEOUT=120000 \
    NPM_CONFIG_FETCH_TIMEOUT=300000 \
    NPM_CONFIG_UPDATE_NOTIFIER=false
COPY package.json package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci

FROM deps AS test
COPY . .
RUN npm test

FROM deps AS build
COPY . .
RUN npm run build

FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.d/40-runtime-config.sh /docker-entrypoint.d/40-runtime-config.sh
# The Windows checkout can turn the script's line endings into CRLF, which the shell then
# cannot run; strip them here as well as in .gitattributes, and make it executable.
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-runtime-config.sh \
 && chmod +x /docker-entrypoint.d/40-runtime-config.sh
COPY --from=build /app/dist /usr/share/nginx/html

# Where the BROWSER finds the backend (the browser makes the API calls, not this container).
ENV API_BASE_URL=http://localhost:8080

EXPOSE 80
HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO /dev/null http://127.0.0.1/ || exit 1
