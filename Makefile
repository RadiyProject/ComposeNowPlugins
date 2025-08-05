up:
	cd _docker && docker-compose --env-file ../.env up -d

down:
	cd _docker && docker-compose --env-file ../.env down

restart:
	make down
	make up