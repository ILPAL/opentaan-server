# syntax=docker/dockerfile:1

# Build from the repository root, because the API code is generated from
# contracts/openapi, which lives outside this folder:
#
#   docker build -f admin-server/Dockerfile .
#
# Dockerfile.dockerignore next to this file limits the context to what the build needs.

# ---- build ----
FROM eclipse-temurin:25-jdk AS build
WORKDIR /workspace/admin-server

# Build files first, so a source change does not re-download dependencies.
COPY admin-server/.mvn/ .mvn/
COPY admin-server/mvnw admin-server/pom.xml ./
RUN ./mvnw -B -q dependency:go-offline

COPY contracts/openapi/ /workspace/contracts/openapi/
COPY admin-server/src/main/ src/main/
RUN ./mvnw -B -q package -DskipTests

# ---- runtime ----
FROM eclipse-temurin:25-jre AS runtime
RUN useradd --system --uid 10001 --no-create-home opentaan
WORKDIR /app
COPY --from=build /workspace/admin-server/target/admin-server-*.jar app.jar

USER opentaan
EXPOSE 8080
ENTRYPOINT ["java", "-jar", "/app/app.jar"]
