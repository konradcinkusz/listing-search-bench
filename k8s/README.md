# Kubernetes manifests

Illustrative, not deployed. This POC has no cluster wired to it (`docs/DEVIATIONS.md`
D-4, D-5) — these manifests exist to answer the JD's "Docker und Kubernetes" line
with something concrete to review, not to claim a running deployment.

| File | What it declares |
|---|---|
| [`namespace.yaml`](namespace.yaml) | The `homefinder` namespace |
| [`configmap.yaml`](configmap.yaml) | Non-secret configuration — `SearchIndex__Mode`, ports |
| [`deployment.yaml`](deployment.yaml) | The search service, 2 replicas, resource requests/limits, readiness/liveness against `/health` and `/alive` |
| [`service.yaml`](service.yaml) | A `ClusterIP` service in front of the deployment |
| [`hpa.yaml`](hpa.yaml) | A `HorizontalPodAutoscaler` scaling on CPU, 2–6 replicas |

Apply order: `kubectl apply -f namespace.yaml -f configmap.yaml -f deployment.yaml -f
service.yaml -f hpa.yaml`, or `kubectl apply -f k8s/` (namespace-scoped resources
apply fine in any order once the namespace itself exists).

**What is deliberately absent.** No `Ingress` (no public deployment exists to route
to — `docs/DEVIATIONS.md`), no `Secret` (this service needs none — `SearchIndex:Mode`
defaults to the credential-free fixture index, ADR-0002), no `StatefulSet` for
Elasticsearch (a managed or separately-operated cluster is the realistic production
shape; `docker-compose.yml` is what local development runs instead).
