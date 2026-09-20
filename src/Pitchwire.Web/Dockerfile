FROM node:24-alpine AS build
WORKDIR /source

# The lockfile first, so an install layer is reused whenever only source has changed.
COPY src/Pitchwire.Web/package.json src/Pitchwire.Web/package-lock.json ./
RUN npm ci

COPY src/Pitchwire.Web/ ./
RUN npm run build && node scripts/bundle-budget.mjs

FROM nginx:1-alpine AS runtime

# nginx has no $connection_upgrade of its own. Without this map the hub connects and then hangs.
RUN printf 'map $http_upgrade $connection_upgrade {\n  default upgrade;\n  "" close;\n}\n' \
    > /etc/nginx/conf.d/upgrade.conf

COPY src/Pitchwire.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /source/dist /usr/share/nginx/html

EXPOSE 8080
