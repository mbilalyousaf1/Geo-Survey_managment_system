FROM node:24.14.1-bookworm-slim AS build
WORKDIR /web
COPY src/Frontend/geosurvey-web/package.json src/Frontend/geosurvey-web/package-lock.json ./
RUN npm ci
COPY src/Frontend/geosurvey-web/ ./
RUN npm run build

FROM nginx:stable-alpine
COPY deploy/docker/phase1-nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/geosurvey-web/browser /usr/share/nginx/html
EXPOSE 8443
