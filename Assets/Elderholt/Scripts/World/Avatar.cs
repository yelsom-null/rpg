using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Avatar — a blocky low-poly figure (torso, head, two legs, two arms) with a
    //  hand-rolled walk/mine animation ported from the prototype's render loop.
    //  Positions are driven by the interpolated snapshot; limbs swing locally.
    // ============================================================================
    public class Avatar
    {
        public readonly Transform root;
        readonly Transform legL, legR, armL, armR;
        float legLX, legRX, armLX, armRX;
        float bob;

        public Avatar(string name, Color shirt, Transform parent)
        {
            root = new GameObject("Avatar_" + name).transform;
            root.SetParent(parent, false);

            GameObject torso = Geo.Primitive(PrimitiveType.Cube, shirt, root);
            torso.transform.localScale = new Vector3(0.6f, 0.75f, 0.35f);
            torso.transform.localPosition = new Vector3(0, 1.0f, 0);

            GameObject head = Geo.Primitive(PrimitiveType.Sphere, Geo.Skin, root);
            head.transform.localScale = Vector3.one * 0.52f; // r = 0.26
            head.transform.localPosition = new Vector3(0, 1.65f, 0);

            legL = MakeLimb("LegL", new Vector3(0.2f, 0.6f, 0.2f), new Vector3(-0.16f, 0.32f, 0), Geo.Legs);
            legR = MakeLimb("LegR", new Vector3(0.2f, 0.6f, 0.2f), new Vector3(0.16f, 0.32f, 0), Geo.Legs);
            armL = MakeLimb("ArmL", new Vector3(0.14f, 0.6f, 0.14f), new Vector3(-0.4f, 1.0f, 0), shirt);
            armR = MakeLimb("ArmR", new Vector3(0.14f, 0.6f, 0.14f), new Vector3(0.4f, 1.0f, 0), shirt);
        }

        Transform MakeLimb(string name, Vector3 scale, Vector3 pos, Color color)
        {
            GameObject go = Geo.Primitive(PrimitiveType.Cube, color, root);
            go.name = name;
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            return go.transform;
        }

        float targetYawDeg;

        public void Place(float x, float z, float dir)
        {
            root.position = new Vector3(x, 0, z);
            targetYawDeg = dir * Mathf.Rad2Deg;
        }

        public void Animate(string anim, float dt)
        {
            // "Turn is free but visible": rotate toward heading at ~10 rad/s so
            // direction changes read as animation, not teleport.
            float cur = root.eulerAngles.y;
            float next = Mathf.MoveTowardsAngle(cur, targetYawDeg, 10f * Mathf.Rad2Deg * dt);
            root.rotation = Quaternion.Euler(0, next, 0);

            if (anim == "walk")
            {
                bob += dt * 11f;
                legLX = Mathf.Sin(bob) * 0.7f;
                legRX = -Mathf.Sin(bob) * 0.7f;
                armLX = -Mathf.Sin(bob) * 0.5f;
                armRX = Mathf.Sin(bob) * 0.5f;
            }
            else if (anim == "run")
            {
                // Faster stride, longer swing — the run lean sells the gait.
                bob += dt * 17f;
                legLX = Mathf.Sin(bob) * 1.0f;
                legRX = -Mathf.Sin(bob) * 1.0f;
                armLX = -Mathf.Sin(bob) * 0.8f;
                armRX = Mathf.Sin(bob) * 0.8f;
            }
            else if (anim == "trudge")
            {
                // Heavy carry: slow, short steps, arms hanging.
                bob += dt * 6f;
                legLX = Mathf.Sin(bob) * 0.4f;
                legRX = -Mathf.Sin(bob) * 0.4f;
                armLX = -0.15f; armRX = -0.15f;
            }
            else if (anim == "mine")
            {
                bob += dt * 10f;
                armRX = Mathf.Sin(bob) * 0.9f - 0.5f;
                legLX *= 0.8f; legRX *= 0.8f; armLX *= 0.8f;
            }
            else if (anim == "wedge")
            {
                // Braced low, both arms forward on the wedge.
                armLX = -1.1f; armRX = -1.1f;
                legLX *= 0.8f; legRX *= 0.8f;
            }
            else if (anim == "smith")
            {
                // Quick hammer taps with the right arm.
                bob += dt * 14f;
                armRX = Mathf.Sin(bob) * 0.7f - 0.7f;
                legLX *= 0.8f; legRX *= 0.8f; armLX *= 0.8f;
            }
            else
            {
                legLX *= 0.8f; legRX *= 0.8f; armLX *= 0.8f; armRX *= 0.8f;
            }

            legL.localRotation = Quaternion.Euler(legLX * Mathf.Rad2Deg, 0, 0);
            legR.localRotation = Quaternion.Euler(legRX * Mathf.Rad2Deg, 0, 0);
            armL.localRotation = Quaternion.Euler(armLX * Mathf.Rad2Deg, 0, 0);
            armR.localRotation = Quaternion.Euler(armRX * Mathf.Rad2Deg, 0, 0);
        }
    }
}
