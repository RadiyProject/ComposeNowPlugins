-include .env

PLUGIN ?= none
ENV ?= local
COMPOSE_FILE := docker-compose.${ENV}.yml
ENV_FILE := ../.env
DOCKER_COMPOSE := docker compose --env-file ${ENV_FILE}

up:
	cd _docker && ${DOCKER_COMPOSE} -f ${COMPOSE_FILE} up -d --no-build

down:
	cd _docker && ${DOCKER_COMPOSE} -f ${COMPOSE_FILE} down

restart:
	make down ENV=${ENV}
	make up ENV=${ENV}

restore:
	dotnet restore src/Proxy/Proxy.csproj
	dotnet restore src/Worker/Worker.csproj

rebuild:
	cd _docker && ${DOCKER_COMPOSE} -f ${COMPOSE_FILE} build

shell:
	cd _docker && ${DOCKER_COMPOSE} -f ${COMPOSE_FILE} exec plugins-proxy bash

build:
	- cd _docker && ${DOCKER_COMPOSE} -f docker-compose.artifacts.yml up --build --abort-on-container-exit
	cd _docker && ${DOCKER_COMPOSE} -f docker-compose.artifacts.yml down
