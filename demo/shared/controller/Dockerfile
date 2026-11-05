FROM golang@sha256:0c050feb47802ae436b9030833694a0b80160d08e3b3049ad30bf7c9e724e09d AS build

WORKDIR /demo
COPY . /demo

RUN cd /demo/shared/controller && CGO_ENABLED=0 go build -trimpath -ldflags "-s -w" -o /out/controller .

FROM alpine@sha256:ce9e1e6d23f04a0b7593445a062a9fc73ed52f8260b9396f77a958295ad18ee7

RUN apk add --no-cache docker-cli docker-cli-buildx docker-cli-compose

WORKDIR /demo
COPY . /demo

COPY --from=build /out/controller /demo/controller
EXPOSE 80

ENTRYPOINT ["/demo/controller"]
