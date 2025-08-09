PLUGIN ?= none

up:
	cd _docker && docker compose --env-file ../.env up -d

down:
	cd _docker && docker compose --env-file ../.env down

restart:
	make down
	make up

shell:
	cd _docker && docker compose --env-file ../.env exec vst-host bash

build:
	cd _docker && docker compose --env-file ../.env -f docker-compose.build.yml up -d --build
	cd _docker && docker compose exec vst-host-build cd /plugins/${PLUGIN}/build && cmake .. && make
	cd _docker && docker compose --env-file ../.env -f docker-compose.build.yml down
