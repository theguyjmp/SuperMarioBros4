; Bosses shared declarations (bosses agent). Included by snes/src/ent_boss_*.s only.
.global boss_activate, boss_music, boss_player_near, boss_defeated, boss_approach, boss_ring_vel, boss_result
.global bm_dx, bm_dy

; orb results (BOSS_ORB arg): the engine's RES_* when eng.inc defines them
.ifdef RES_FORTRESS
BOSS_R_FORTRESS = RES_FORTRESS
BOSS_R_WORLD = RES_WORLD
.else
BOSS_R_FORTRESS = 3
BOSS_R_WORLD = 4
.endif

; C# Settings.ReduceFlashing / ScreenShake are not in the ROM yet: flashing = normal, shake = on
BOSS_FLASH_MASK = 2
