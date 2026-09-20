# busted on LuaJIT, which is what KOReader runs. Built once, reused by spec/run.sh.
FROM nickblah/luajit:2.1-luarocks
RUN apt-get update -qq && apt-get install -y -qq --no-install-recommends build-essential >/dev/null \
    && luarocks install busted >/dev/null \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /plugin
ENTRYPOINT ["busted"]
