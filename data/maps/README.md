# World map format (data/maps/wN.map)

```
name  = MUSHROOM MEADOWS      # shown when the world starts
music = map1                  # optional (default mapN)
pal   = map.w1                # optional (default map.wN)
node 1 = 1-1                  # level panels 1-9 -> level ids
node F = 1-f                  # fortress (G = second fortress)
node A = 1-a                  # airship (ends the world)   | node B = 8-c  Bowser's castle (world 8)
node H = toad:mushroom,flower,leaf   # Toad house: 3 chests drawn from this pool (mushroom flower leaf star pwing tanooki frog hammer cloud)
node P = spade                # spade bonus game (slot machine)
lock L = F                    # lock gate L opens when node F is cleared (M = second lock)
grid
~~~~~~~~~~~~~~~~
~.S-1-+-2......~
...  (16 columns x 12 rows, may be wider for scrolling maps)
end
```

Grid characters. Scenery: `.` land, `,` land2, `~` water, `T` tree, `^` hill, `r` rock, `*` flowers, `p` palm, `s` sand,
`w` snow, `c` cloud, `v` lava, `k` skull. Paths: `-` horizontal, `|` vertical, `=` bridge (horizontal), `!` bridge
(vertical), `+` resting dot (a stop). Nodes: `S` start, `1`-`9` levels, `F` `G` fortresses, `A` airship, `B` castle,
`H` Toad house, `P` spade panel, `L` `M` locks.

Rules (SMB3): paths connect nodes orthogonally; the player walks from stop to stop. An uncleared level/fortress/airship
panel can only be left the way you came in, so every level on the main route must be cleared. Locks block the path
until their fortress is cleared. Keep the world's route readable at a glance.
