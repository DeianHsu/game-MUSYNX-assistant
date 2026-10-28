
import UnityPy, shutil, sys, os
src = r"D:\Game\Steam\steamapps\common\MUSYNX\MUSYNX_Data\globalgamemanagers"
tmp = r"D:\MyAIWork\DSH Desktop\workspace\MUSYNX\.tmp\ggm_test"
shutil.copy2(src, tmp)
env = UnityPy.load(tmp)
target = None
for obj in env.objects:
    if obj.type.name == "PlayerSettings":
        target = obj
        break
if target is None:
    print("NO PlayerSettings"); sys.exit(1)
t = target.read_typetree()
print("before:", t.get("displayResolutionDialog"))
t["displayResolutionDialog"] = 0
target.save_typetree(t)
data = env.file.save()
print("saved bytes:", type(data), len(data) if data else 0)
if data:
    open(tmp, "wb").write(data)
env2 = UnityPy.load(tmp)
for obj in env2.objects:
    if obj.type.name == "PlayerSettings":
        print("after:", obj.read_typetree().get("displayResolutionDialog"))
        break
print("size", os.path.getsize(tmp), os.path.getsize(src))
