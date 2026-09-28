-- Takes away the fake reading tools/dev/seed-reading.sql made for a reader, and nothing else:
-- only sessions stamped as logged at 2000-01-01T00:00:00Z, which no real session is.
--
-- LOCAL DATABASE ONLY.
--
--   docker compose exec -T postgres sh -lc 'psql -U $POSTGRES_USER -d $POSTGRES_DB -v reader=dev-reader' < tools/dev/unseed-reading.sql

delete from library."ReadingSessions" s
using library."LibraryEntries" e
where s."LibraryEntryId" = e."Id"
  and e."ReaderId" = :'reader'
  and s."LoggedAt" = timestamptz '2000-01-01T00:00:00Z';
