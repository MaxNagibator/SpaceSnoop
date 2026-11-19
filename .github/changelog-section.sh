#!/usr/bin/env bash
# Печатает раздел CHANGELOG.md для указанной версии. Нет раздела – ненулевой код возврата.
set -euo pipefail

VERSION="${1:?версия не передана}"
FILE="${2:-CHANGELOG.md}"

section=$(awk -v heading="## $VERSION" '
  { sub(/\r$/, "") }
  $0 == heading { found = 1; next }
  found && /^## / { exit }
  found { print }
' "$FILE" | sed -e '/./,$!d')

if [ -z "$(printf '%s' "$section" | tr -d '[:space:]')" ]; then
  echo "CHANGELOG.md: нет раздела «## $VERSION» или он пуст" >&2
  exit 1
fi

printf '%s\n' "$section"
