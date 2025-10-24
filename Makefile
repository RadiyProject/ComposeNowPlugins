PLUGIN ?= none

up:
	cd _docker && docker compose --env-file ../.env up -d --no-build

down:
	cd _docker && docker compose --env-file ../.env down

restart:
	make down
	make up

shell:
	cd _docker && docker compose --env-file ../.env exec vst-host bash

build:
#--build
	cd _docker && docker compose --env-file ../.env -f docker-compose.build.yml up --no-build --abort-on-container-exit
	cd _docker && docker compose --env-file ../.env -f docker-compose.build.yml down
