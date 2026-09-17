# Features

### Player ESP
- Bounding Boxes: 2D boxes around players, color-coded by team/role
- 17-Bone Skeleton: Wireframe skeleton rendering supporting human, SCP-106, and SCP-049-2 hierarchies
- Name Tags: Player names with distance display
- Health Bars: Vertical health bars with current and maximum HP
- Distance Filter: Configurable render distance

### Ground Item ESP
- Mirror Network Pickup Scanning: Scans spawned ground items
- GPU Icon Rendering: Transparent item icons rendered via Direct3D 11
- 3 Display Styles: Icon + Text, Icon Only, Text Only
- Category Filters: Keycards, Weapons, Ammo, Armor, Medical, SCP Items, Utility

### Smart Inventory-Aware Culling
- Max Ammo Capacity: Automatically hides ground ammo when reserve ammo is full for that caliber
- Best-in-Slot Armor: Carrying or wearing Heavy Armor hides ground armor; Combat hides Light
- Keycard Hierarchy Matching: Hides inferior keycards when holding superior cards
- Caliber Compatibility: Culls ground ammo boxes not matching weapons in inventory
- Duplicate Item Culling: Hides redundant keycards and duplicate utility items

### World ESP
- Room ESP: Displays facility room names with distance, filtered by active zone
- Generator ESP: Displays SCP-079 generators with live status and countdown timers

### HUD Panel
- Live Player Counts: Alive and Dead player counts
- Alpha Warhead Tracker: Detonation countdown timer
- Generator Progress: Counter of engaged generators (X/3)
- Round Timer & Status: Current round duration and status

### Memory Writes
- World Camera FOV: Custom player camera FOV (60°-120°)
- Hold-to-Zoom: Dedicated zoom FOV (15°-60°) with customizable keybind
- Viewmodel FOV: Custom viewmodel FOV (30°-140°)
- No Weapon Sway & Bobbing: Removes weapon sway, breathing, and walking bobbing
- Weapon Recoil Control: Scales or eliminates vertical/horizontal recoil (0%-100%)
- Auto-Bunnyhop: Automatic jump timing when grounded while holding Space

### Keybinds

| Key | Action |
|---|---|
| F1 / Insert | Toggle menu |
| F7 | Toggle HUD drag |
| End | Exit |
