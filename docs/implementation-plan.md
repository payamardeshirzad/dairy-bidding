In scope (must build first)

IdentityService can issue token (dev mode acceptable)
BiddingService:
GET /auctions/active
POST /bids
Save bids in PostgreSQL
Publish BidPlaced event to RabbitMQ
Optional: cache active auctions in Redis
Out of scope (not in first slice)

Elasticsearch indexing/search
MinIO file flows
Payment flow
Full notification pipeline
Advanced dashboards/alerts
Acceptance criteria

Can request token
Can call protected bid endpoint with token
Bid row appears in DB
BidPlaced event appears in RabbitMQ exchange/queue
Basic tests pass