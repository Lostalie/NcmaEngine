# FBX regression fixtures

These unmodified files come from the `data/` directory of
https://github.com/ufbx/ufbx at commit `fcc5d6ba444cfd3eb80677dba5e37e493941abe5`
(v0.23.0). License: the upstream MIT / Unlicense, retained at
`engine/sdk/ufbx/LICENSE`; Ncma uses the MIT option.

They exercise a Blender-exported skinned, animated character in ASCII FBX 6100
and binary FBX 7400. They are test assets, not game content.

SHA-256:

- blender_279_sausage_6100_ascii.fbx: `027a56de80abe7a6adc873ee3b5bb306b127322ec5dbe742c284b17ec37071d9`
- blender_279_sausage_7400_binary.fbx: `d30acc615d59a2dc6c68ba6794849cd9d968b1beb883799c66c88a90525a9287`

Ncma-authored synthetic fixtures (CC0, no user/third-party game content):

- ncma_static_asymmetric_7400_ascii.fbx: `2c993e1517fd12d693c14acce6f72e4d780c7180e3ac421e92aee46ef4ddca46`.
  RH Y-up, centimetres, asymmetric triangle/translation/nonuniform scale and explicit UVs.
- ncma_static_instances_zup_7400_ascii.fbx: `60d9c41de7aa91bb8b9494feef02b0bd63c3b9d6034cd566f96ba9557d88215e`.
  RH Z-up, metres, two shared-geometry static instances and missing UVs.
- ncma_skin_weights_7400_ascii.fbx: `ca55ede801e816727a3ef8cd84a0f98a627877d6b5f04dfa33789a448ef60672`.
  Five equal skin influences on the first two vertices and an unweighted final vertex.

Clip reordering/topology/rig mutation tests additionally use copied numerical fixture data in ignored test output;
they are not claims that every DCC multi-clip FBX has been validated. Duplicate-source-name negative inputs
are derived only under ignored test output. Frozen fixtures are never regenerated to conceal a regression.
