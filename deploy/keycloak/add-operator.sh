#!/usr/bin/env bash
# Adds an operator to the local Keycloak realm and grants them roles.
# Local development only: it talks to the Keycloak in docker-compose.yml.
#
#   deploy/keycloak/add-operator.sh <username> <role> [<role> ...]
#
# Roles: viewer, support, moderator, economy, operations, auditor, admin.
# The password is prompted for, or read from OPERATOR_PASSWORD (for scripts and CI).
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: $0 <username> <role> [<role> ...]" >&2
  exit 64
fi

cd "$(dirname "$0")/../.."
username=$1
shift

if [[ -n "${OPERATOR_PASSWORD:-}" ]]; then
  password=$OPERATOR_PASSWORD
else
  read -r -s -p "Password for $username: " password
  echo
fi
if [[ ${#password} -lt 12 ]]; then
  echo "Use a password of at least 12 characters." >&2
  exit 65
fi

kcadm() {
  docker compose exec -T keycloak /opt/keycloak/bin/kcadm.sh "$@"
}

kcadm config credentials --server http://localhost:8080 --realm master \
  --user admin --password "$(< secrets/keycloak_admin_password)" >/dev/null

# Keycloak's user profile requires a name and an email before a user can sign in.
kcadm create users --target-realm opentaan \
  --set username="$username" --set enabled=true \
  --set firstName="$username" --set lastName=operator \
  --set email="$username@operators.opentaan.invalid" --set emailVerified=true >/dev/null
kcadm set-password --target-realm opentaan --username "$username" --new-password "$password"
for role in "$@"; do
  kcadm add-roles --target-realm opentaan --uusername "$username" --rolename "$role"
done

echo "Added $username with roles: $*"
