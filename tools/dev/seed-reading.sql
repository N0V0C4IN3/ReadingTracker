-- Fake reading for trying the stats page locally: a year and more of sessions against a
-- reader's books, so the calendar, the runs and the weekdays have something to show.
--
-- LOCAL DATABASE ONLY. Never run it against the Pi.
--
--   docker compose exec -T postgres sh -lc 'psql -U $POSTGRES_USER -d $POSTGRES_DB -v reader=dev-reader' < tools/dev/seed-reading.sql
--
-- Optional: -v days=400 (how far back to go; the default reaches into last year, so the year
-- switch has a year behind it too).
--
-- Every session it makes is stamped as logged at 2000-01-01T00:00:00Z, which no real session
-- is, so running it again replaces its own sessions and tools/dev/unseed-reading.sql takes them
-- away without touching anything the reader logged themselves. The sessions count toward the
-- books' progress while they are there.
--
-- The shape: most days read, weekends more often and more; the last ten days all read, so there
-- is a run going; one or two books a day, among the ones being read or finished (every book but
-- the wanted-to-read ones); about half the sittings from a device, most with a duration.

\if :{?days}
\else
\set days 400
\endif

begin;

delete from library."ReadingSessions" s
using library."LibraryEntries" e
where s."LibraryEntryId" = e."Id"
  and e."ReaderId" = :'reader'
  and s."LoggedAt" = timestamptz '2000-01-01T00:00:00Z';

with books as (
    select "Id"
    from library."LibraryEntries"
    where "ReaderId" = :'reader' and "Status" <> 'WantToRead'
),
days as (
    select d::date as day, extract(isodow from d) >= 6 as weekend
    from generate_series(current_date - :days, current_date, interval '1 day') as d
),
reading_days as (
    -- How many sittings is decided here, a day at a time: in generate_series below it would be
    -- decided once for every day.
    select day, weekend, case when random() < 0.3 then 2 else 1 end as sittings
    from days
    where day > current_date - 10
       or random() < case when weekend then 0.85 else 0.62 end
),
sittings as (
    select day, weekend, n
    from reading_days, generate_series(1, reading_days.sittings) as n
),
made as (
    select
        (select "Id" from books where s.n is not null order by random() limit 1) as entry,
        (s.day + make_interval(hours => 7 + floor(random() * 16)::int, mins => floor(random() * 60)::int))::timestamp at time zone 'UTC' as occurred,
        (8 + floor(random() * 40) + case when s.weekend then 20 else 0 end)::int as pages,
        random() < 0.5 as from_device,
        random() < 0.75 as timed
    from sittings s
)
insert into library."ReadingSessions" ("Id", "LibraryEntryId", "Unit", "OccurredAt", "DurationMinutes", "LoggedAt", "Amount", "Source")
select
    gen_random_uuid(),
    entry,
    'Pages',
    occurred,
    case when timed then pages * 2 + floor(random() * 10)::int end,
    timestamptz '2000-01-01T00:00:00Z',
    pages,
    case when from_device then 'Device' else 'Reader' end
from made
where entry is not null;

select count(*) as fake_sessions, min("OccurredAt")::date as since, max("OccurredAt")::date as until
from library."ReadingSessions" s
join library."LibraryEntries" e on e."Id" = s."LibraryEntryId"
where e."ReaderId" = :'reader' and s."LoggedAt" = timestamptz '2000-01-01T00:00:00Z';

commit;
