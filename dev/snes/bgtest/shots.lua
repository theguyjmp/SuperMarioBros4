local names = {"plains_dry","underground_dry","desert_dry","sea_dry","sea_wet","sea_mix","jungle_dry","sky_dry","ice_dry","ice_mix","machine_dry","volcano_dry","fortress_dry","fortress_mix","castle_dry","airship_dry","bonus_dry","flat"}
for i, n in ipairs(names) do
  local base = (i - 1) * 120
  at(base + 40, function() save(n .. "_A") end)
  at(base + 100, function() save(n .. "_B") end)
end
at(#names * 120 + 5, function() finish(0) end)
