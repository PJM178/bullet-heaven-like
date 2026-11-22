# Shortcuts for the server side. Run from the repo root: `make up`, `make down`...
# (The game itself is built and run from the Godot editor.)

COMPOSE = docker compose -f deploy/docker-compose.yml

.PHONY: help run up down logs build health

help:   ## List the targets
	@grep -E "^[a-z]+:.*##" $(MAKEFILE_LIST) | sed "s/:.*##/\t/"

run:    ## Build and start the backend in the foreground, logs on screen (Ctrl+C stops it)
	$(COMPOSE) up --build

up:     ## Build and start the backend in Docker (in the background)
	$(COMPOSE) up --build -d

down:   ## Stop and remove the backend container
	$(COMPOSE) down

logs:   ## Follow the backend logs (Ctrl+C to stop following)
	$(COMPOSE) logs -f

build:  ## Build every project (backend, shared, game) with dotnet
	dotnet build BulletHeavenLike.slnx

health: ## Ask the running backend if it is up
	curl -s http://localhost:8080/health; echo
