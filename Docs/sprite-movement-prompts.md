# Movement / Animation Prompt Set — Drill-Mech Character

Stage 3 of the sprite pipeline. Stages 1–2 (south-facing identity anchor,
directional anchors) produce the neutral reference frames; these prompts
generate the **movement frames** from those anchors.

## Character fill-ins for this unit

Use these values wherever the templates reference the variables:

| Variable | Value |
|---|---|
| `{CHARACTER_NAME}` | Drill-Mech Unit ("Borehound") |
| `{ARCHETYPE}` | heavy mining/combat mech |
| `{SILHOUETTE_NOTES}` | broad armored shoulders, compact head with single horizontal red visor slit, blocky segmented limbs |
| `{COSTUME_DETAILS}` | dark navy/gunmetal plate armor with bronze-brown joint hardware, exposed piston/gear detailing at torso, hips, and knees |
| `{PROP_DETAILS}` | oversized conical drill mounted on the right forearm; rocket booster / thruster pod on the left shoulder with small stabilizer fins |
| `{DIRECTIONAL_SILHOUETTE_DETAILS}` | drill arm reads as the widest mass on the character's right; booster pod breaks the left shoulder line; visor slit only visible on south/east/west frames |

Identity constants (repeat in every prompt): red glowing visor, top-left
light source, chunky readable pixel silhouette, flat chroma background
(#FF00FF), no anti-aliased halos.

---

## Master movement-frame template

```
Intended use: one frame of a {ANIMATION_NAME} animation for a top-down 2D action game character.
Final artwork should behave like one logical {LOGICAL_FRAME_SIZE} in-game frame, delivered at
{OUTPUT_SIZE} so each sprite pixel reads as a clean block.

Input images:
Image 1 is the approved {DIRECTION}-facing anchor for {CHARACTER_NAME}. Preserve the same character
identity, outfit, palette, proportions, silhouette, accessories, and high-resolution pixelated
game-sprite style.
Image 2 (optional) is the previous frame of this animation. Use it only for motion continuity —
limb positions must read as the next step of the same motion.

Primary request:
Create frame {FRAME_INDEX} of {FRAME_COUNT} of a {ANIMATION_NAME} cycle for the same character,
facing {DIRECTION_DESCRIPTION}, in a game-ready top-down view.

Pose for this frame:
- {FRAME_POSE_DESCRIPTION}
- Keep the torso, head, and accessories consistent with the anchor.
- Anchor/foot plant stays at bottom-center; the body may bob at most 1-2 logical pixels vertically.
- Preserve readable direction-specific silhouette: {DIRECTIONAL_SILHOUETTE_DETAILS}.

Composition:
- Single centered character.
- Full body visible, ample padding on all sides.
- Same logical sprite box and scale as the anchor — do NOT zoom, crop, or reframe.
- Flat chroma background matching source-anchor style (#FF00FF), no shadow, props, UI, or text.

Critical constraints:
- No motion blur, speed lines, afterimages, or smears.
- No new accessories, weapons, or effects unless this animation explicitly adds them.
- Palette identical to the anchor.

Style:
- High-resolution pixelated 2D game sprite, crisp readable silhouette,
  limited surface shading plus small highlight pixels, consistent top-left light source.
```

---

## Animation specs (fill `{FRAME_POSE_DESCRIPTION}` per frame)

### 1. Idle bob — 4 frames, per direction (S first)
Loop: 1→2→3→4→1

1. Neutral anchor pose (this IS the anchor frame; regenerate only if the anchor wasn't padded to match).
2. Body settles 1 logical pixel down; shoulder armor compresses slightly; booster pod tips 1px.
3. Lowest point: 2px down, knees flex a hint, visor glow 1 shade brighter.
4. Rising back through 1px down; drill arm drifts 1px outward.

### 2. Walk cycle — 6 frames, per direction (S, N, E, W)
Contact–down–passing–contact–down–passing. Heavy mech gait: deliberate, stompy.

1. Left foot forward contact, right foot back, arms counter-swing (drill arm swings less — it's heavy).
2. Weight drops onto left foot, body 1px down, booster pod dips.
3. Passing pose: feet together, body at tallest, pistons at knees visibly extended.
4. Right foot forward contact (mirror of 1).
5. Weight drops onto right foot (mirror of 2), drill tip nearly grazes ground on S/E frames.
6. Passing pose (mirror of 3).

East/west: drill arm side leads the silhouette; keep the drill from crossing the body's centerline.
North: visor not visible; booster pod and back plating carry the read.

### 3. Drill attack — 5 frames, per direction
Not a loop; returns to idle.

1. Wind-up: drill arm pulls back and up, torso twists away, feet widen.
2. Anticipation hold: 1 frame, drill tip sparks with 2-3 single highlight pixels (no aura/glow field).
3. Thrust: drill arm fully extended forward at waist height, torso leans in, rear foot pushes off.
4. Impact hold: drill extended, small debris pixels (max 4-6 pixels) at the tip, body at maximum lean.
5. Recovery: arm retracting halfway back toward idle, torso straightening.

### 4. Jet dash — 3 frames, per direction
Horizontal burst using the shoulder booster.

1. Crouch: knees bent 2-3px, booster pod rotates to point backward.
2. Burst: body leans hard into travel direction, feet trail, short 3-4px pixel exhaust cone from booster (solid pixels, no gradient glow).
3. Skid recovery: feet re-plant wide, body upright, 1-2 dust pixels at feet.

### 5. Hit reaction — 2 frames, per direction
1. Flinch: body knocked 2px opposite the facing direction, visor flickers (1 shade dimmer), shoulder armor pops 1px loose-looking.
2. Recover: settling back to neutral, everything reseated.

---

## Generation order & QA checklist

1. Generate S-direction idle first; it must diff-overlay cleanly onto the S anchor (only intended limbs move).
2. Then S walk, then remaining directions, then attacks/dash/hit.
3. Reject a frame if: palette drifted, sprite box scale changed, drill/booster swapped sides,
   background isn't flat chroma, or the silhouette gains anti-aliased halo pixels.
4. Feed each accepted frame as Image 2 of the next frame's prompt for continuity.

## Running these through Unity MCP

With the Unity Editor open and an image provider configured (fal.ai or OpenRouter,
key stored in the editor's secure store), each frame is one `generate_image` call:
`mode=image`, `image_path=<anchor or previous frame>`, `prompt=<filled template>`,
`transparent=false` (chroma-key later), `output_folder=Assets/Elderholt/Resources/Sprites/Borehound`.
