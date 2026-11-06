# 01: 工程与场景基线

**What to build:** Unity 6000.3.28f1 URP 工程建在仓库内（Unity 标准 .gitignore）；按 asset-route 02 票管线导入场景 FBX、Extract Textures、转 URP；按 06 票结论将前柜（含收银、Bar_Rack、吧凳、吧台吊灯）北移约 0.9m 使前后柜通道 ≥1.2m。完成后在编辑器中打开场景，全店正确显示、材质颜色正常、通道实测 ≥1.2m。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] Unity URP 工程在仓库内可打开，批处理构建脚本可用（注意先 export PROGRAMDATA）
- [ ] 场景导入后 120 个对象结构完整，包围盒约 10.2×4.5×13.0 m
- [ ] 纹理经 Extract Textures 绑定，材质已转 URP，颜色与参考一致
- [ ] 前柜北移后通道实测 ≥1.2m，吧凳/灯架随移无穿插
