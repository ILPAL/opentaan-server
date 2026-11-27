# Client data

Data the game server needs that comes from the game client. The client is someone
else's work, so nothing here goes into git (see `.gitignore` here and
[ADR 0010](../docs/adr/0010-no-game-client-files.md)): whoever runs the server keeps
their own copy, made from their own client.

The server finds this folder by looking up from where it starts, so `dotnet run`
finds it from the repository. `docker-compose.yml` mounts it read-only into the game
server's container.

| File | Used for | Setting |
|---|---|---|
| `item-drops.json` | The items an item box or a bonus gives in a match between teams | `Matches:ItemDropsFile` |

The server does not start without `item-drops.json`, and says which file it looked
for and where.

## `item-drops.json`

The rows of the client's item drop table, as the client has them: each item a box can
give, the drop qualities it drops at, and how likely it is at each end of them. The
server draws every item in the table as likely as any other, so for now only the item
ids matter, but every field must be there, and no other.

```json
{
  "drops": [
    { "itemId": 2561, "minQuality": 0, "maxQuality": 100, "weightAtMin": 10, "weightAtMax": 20 },
    { "itemId": 2562, "minQuality": 40, "maxQuality": 100, "weightAtMin": 1, "weightAtMax": 5 }
  ]
}
```

These ids are made up. A server given them starts, and its boxes give items no real
client has, which is enough for a check that does not play.
