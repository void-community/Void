FROM golang@sha256:87f49b77a7b6a99ad7dc656be010ae453322459dd2ed902ac177649a302bc101 AS build

WORKDIR /demo
COPY . /demo

RUN cd /demo/shared/controller && CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o /out/controller .

FROM alpine@sha256:f59abe656b692d901e184be3445e084786602108599d2a51d229c0f5a48c71bf

RUN apk add --no-cache docker-cli docker-cli-buildx docker-cli-compose

WORKDIR /demo
COPY . /demo

COPY --from=build /out/controller /demo/controller
EXPOSE 80

ENTRYPOINT ["/demo/controller"]
