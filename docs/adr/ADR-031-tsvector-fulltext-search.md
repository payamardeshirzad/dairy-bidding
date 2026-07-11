# ADR-031: PostgreSQL TSVECTOR full-text search as Elasticsearch fallback

## Status
Accepted — Not yet implemented

## Context
Buyers search for dairy products by keyword (breed, region, certifications). Elasticsearch provides ranked full-text search but introduces an additional service to operate and keep in sync. During early phases, Elasticsearch may not be running.

## Decision
`products.search_vector` is a `TSVECTOR` column populated and maintained by a PostgreSQL trigger:

```sql
ALTER TABLE products ADD COLUMN search_vector TSVECTOR;

CREATE TRIGGER products_search_vector_update
BEFORE INSERT OR UPDATE ON products
FOR EACH ROW EXECUTE FUNCTION
  tsvector_update_trigger(search_vector, 'pg_catalog.english', name, description, tags);
```

Search queries use `@@` operator with `to_tsquery`. A `GIN` index on `search_vector` makes queries fast.

When Elasticsearch becomes operational, search traffic is routed to it and the PostgreSQL trigger is kept as a fallback.

**Rejected**: LIKE wildcards (`WHERE name LIKE '%charolais%'`) — no relevance ranking, no morphological analysis; Elasticsearch-only (operational complexity).

## Consequences
- (+) Ranked full-text search without Elasticsearch during development
- (+) Zero additional infrastructure required
- (-) No semantic search, no fuzzy matching — PostgreSQL FTS is lexeme-based
- (-) Keeping Elasticsearch and TSVECTOR in sync when Elasticsearch is active requires careful cutover logic
