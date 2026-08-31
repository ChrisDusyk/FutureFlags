{{/* Naming. */}}

{{- define "futureflags.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "futureflags.fullname" -}}
{{- if .Values.fullnameOverride -}}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name (include "futureflags.name" .) | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "futureflags.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
app.kubernetes.io/name: {{ include "futureflags.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{- define "futureflags.selectorLabels" -}}
app.kubernetes.io/name: {{ include "futureflags.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/*
The host portion of `origin`. The ingress needs a bare hostname while the auth service needs the
whole origin, so both come from the one value rather than being configured twice and drifting.

Bare meaning no scheme, no port, and no path. An Ingress `host` is a DNS name and nothing else —
`flags.example.com:8443` is rejected — while the origin the browser sends, and therefore the one
Better Auth has to trust, does include the port. Those two differ, so this derives one from the
other rather than asking for both and letting them disagree.
*/}}
{{- define "futureflags.host" -}}
{{- $authority := include "futureflags.origin" . | trimPrefix "https://" | trimPrefix "http://" -}}
{{- $authority = (splitList "/" $authority) | first -}}
{{- (splitList ":" $authority) | first -}}
{{- end -}}

{{/*
`origin`, checked and with any trailing slash removed.

An origin is a scheme, a host, and a port — nothing else. A browser sends
`https://flags.example.com`, so anything else here matches nothing in the trusted-origins list
and sign-in fails with an error saying only that the origin is invalid — at a stranger's first
attempt, long after `helm install` reported success.

A trailing slash is a way of writing a correct value, so it is normalised. A path, a query, or a
missing scheme is not: it means `origin` was read as "the console's URL" rather than the origin a
browser sends, and no rendering of that value would work. Those fail here, where the value is set.
*/}}
{{- define "futureflags.origin" -}}
{{- $origin := required "origin is required, e.g. https://flags.example.com" .Values.origin -}}
{{- include "futureflags.checkedOrigin" (dict "value" $origin "setting" "origin") -}}
{{- end -}}

{{/*
One origin, checked and with any trailing slash removed. Takes `value` and the `setting` it came
from, so the failure names the field the reader has to go and edit.

Shared by `origin` and every entry in `browserOrigins`, because they are the same kind of value
compared against the same header — and a rule applied to one of them but not the other is how the
second grows a quiet exception to the first.
*/}}
{{- define "futureflags.checkedOrigin" -}}
{{- $origin := .value | trimSuffix "/" -}}
{{- $setting := .setting -}}
{{- if not (or (hasPrefix "https://" $origin) (hasPrefix "http://" $origin)) -}}
{{- fail (printf "%s has to start with https:// or http:// — got %q. It is the origin a browser sends, not a hostname." $setting $origin) -}}
{{- end -}}
{{- $rest := $origin | trimPrefix "https://" | trimPrefix "http://" -}}
{{- if or (contains "/" $rest) (contains "?" $rest) (contains "#" $rest) -}}
{{- fail (printf "%s has to be a scheme, a host, and an optional port, with nothing after it — got %q. A browser never sends a path in an Origin header, so this would fail at a first request rather than here. Use the ingress or your proxy to serve the console under a path if you need one." $setting $origin) -}}
{{- end -}}
{{- $origin -}}
{{- end -}}

{{/*
`browserOrigins`, each checked, joined for the environment variable the server reads.
*/}}
{{- define "futureflags.browserOrigins" -}}
{{- $checked := list -}}
{{- range .Values.browserOrigins -}}
{{- $checked = append $checked (include "futureflags.checkedOrigin" (dict "value" . "setting" "browserOrigins")) -}}
{{- end -}}
{{- join "," $checked -}}
{{- end -}}

{{- define "futureflags.serverImage" -}}
{{- printf "%s/%s/futureflags-server:%s" .Values.image.registry .Values.image.repository (.Values.image.tag | default .Chart.AppVersion) -}}
{{- end -}}

{{- define "futureflags.authImage" -}}
{{- printf "%s/%s/futureflags-auth:%s" .Values.image.registry .Values.image.repository (.Values.image.tag | default .Chart.AppVersion) -}}
{{- end -}}

{{/*
The Secret this chart manages, and the two independent choices of where each value comes from.

Deliberately three definitions rather than one. Folding them together couples settings that have
nothing to do with each other: pointing `betterAuth.existingSecret` at a Secret you manage would
also send the database lookups there, and since the chart then created no Secret of its own,
every pod would ask for keys nothing had written.
*/}}
{{- define "futureflags.chartSecretName" -}}
{{- printf "%s-secrets" (include "futureflags.fullname" .) -}}
{{- end -}}

{{- define "futureflags.authSecretName" -}}
{{- default (include "futureflags.chartSecretName" .) .Values.betterAuth.existingSecret -}}
{{- end -}}

{{- define "futureflags.databaseSecretName" -}}
{{- if and (not .Values.postgres.bundled) .Values.postgres.external.existingSecret -}}
{{- .Values.postgres.external.existingSecret -}}
{{- else -}}
{{- include "futureflags.chartSecretName" . -}}
{{- end -}}
{{- end -}}

{{/* Whether the chart still has to supply anything of its own. */}}
{{- define "futureflags.needsChartSecret" -}}
{{- if or (not .Values.betterAuth.existingSecret) .Values.postgres.bundled (not .Values.postgres.external.existingSecret) -}}
true
{{- end -}}
{{- end -}}

{{/* In-cluster addresses. Neither service is reachable from outside the namespace. */}}
{{- define "futureflags.authAddress" -}}
{{- printf "http://%s-auth:8080" (include "futureflags.fullname" .) -}}
{{- end -}}

{{- define "futureflags.redisUrl" -}}
{{- if .Values.redis.bundled -}}
{{- printf "redis://%s-redis:6379" (include "futureflags.fullname" .) -}}
{{- else -}}
{{- required "redis.external.url is required when redis.bundled is false" .Values.redis.external.url -}}
{{- end -}}
{{- end -}}

{{/*
Where the database URL comes from, as an env var fragment rather than a plain value — it carries
a password, so it is only ever read from a Secret.

Both the server and the auth service consume it. They share one database and separate by schema,
so this deliberately cannot be configured to two different places.
*/}}
{{- define "futureflags.databaseUrlEnv" -}}
- name: FUTUREFLAGS_DATABASE_URL
  valueFrom:
    secretKeyRef:
      name: {{ include "futureflags.databaseSecretName" . }}
      key: FUTUREFLAGS_DATABASE_URL
{{- end -}}

{{- define "futureflags.betterAuthSecretEnv" -}}
- name: BETTER_AUTH_SECRET
  valueFrom:
    secretKeyRef:
      name: {{ include "futureflags.authSecretName" . }}
      key: BETTER_AUTH_SECRET
{{- end -}}
