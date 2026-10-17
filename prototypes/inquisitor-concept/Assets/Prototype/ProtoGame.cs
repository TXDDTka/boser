// PROTOTYPE - NOT FOR PRODUCTION
// Question: Does the vertical-scroller meat grinder feel good, and does the prison after a loss
//           feel like a reward that makes the player walk back to the portal on their own?
// Date: 2026-10-17

using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Everything in one file on purpose: hub -> run (vertical scroller) -> prison -> hub. Press Play in any scene.
public class ProtoGame : MonoBehaviour
{
    enum Mode { Hub, Run, RunEnd, PrisonMath, PrisonFail, PrisonDialog, PrisonResult }
    enum Effect { None, BuffWhip, DebuffSpeed, DebuffHp }

    // kind: 0 = drifter (snakes down), 1 = swooper (diagonal), 2 = shooter (hovers and fires),
    //       3 = censer (slow, fires rings), 4 = debris obstacle (scrolls with the floor, tanky)
    class Enemy { public GameObject go; public SpriteRenderer sr; public int kind, shots; public Vector2 pos, vel, push, half; public float hp, flash, age, baseX, hoverY, shootTimer; public bool dropShot; }
    class Bullet { public GameObject go; public Vector2 pos; public Vector2 vel; public float life; }
    class Particle { public GameObject go; public SpriteRenderer sr; public Vector2 pos; public Vector2 vel; public float life; public float maxLife; public Color color; }

    static Sprite square;
    static readonly Color DemonColor = new Color(0.78f, 0.15f, 0.78f);
    static readonly Color ShooterColor = new Color(0.55f, 0.1f, 0.6f);
    static readonly Color EnemyShotColor = new Color(1f, 0.35f, 0.75f);
    static readonly Color CenserColor = new Color(0.95f, 0.3f, 0.45f);
    static readonly Color DebrisColor = new Color(0.45f, 0.45f, 0.42f);
    static readonly Color PlayerColor = new Color(0.95f, 0.95f, 0.85f);
    static readonly Color PrayerColor = new Color(1f, 0.85f, 0.3f);

    Camera cam;
    Mode mode;
    Effect effect = Effect.None;
    string lastOutcome = "first"; // first, buff, debuff_dialog, debuff_math, win
    readonly List<GameObject> modeObjects = new List<GameObject>();
    string logPath;
    int guiPick = -1;

    // player
    GameObject player; SpriteRenderer playerSr; Vector2 playerPos;
    int hp, maxHp; float invuln;
    float fireTimer, whipCd, prayerCd, prayerTime;

    // run
    readonly List<Enemy> enemies = new List<Enemy>();
    readonly List<Bullet> bullets = new List<Bullet>();
    readonly List<Bullet> enemyShots = new List<Bullet>();
    readonly List<Particle> particles = new List<Particle>();
    readonly List<GameObject> whipFx = new List<GameObject>();
    readonly List<GameObject> scrollDecor = new List<GameObject>();
    float whipFxTime, runTime, waveTimer, trickleTimer, debrisTimer, hitStop, shake;
    int runIndex, kills, shotsCancelled;
    bool ambushRun;
    string endText, endNext; float endTimer;
    readonly Vector2 field = new Vector2(7.5f, 9f); // half extents of the vertical play column

    // hub
    GameObject tiriona, portal;
    float talkTimer, hubTime; string talkLine; bool talkedThisVisit;

    // prison
    int mathA, mathB, mathC, correctIndex; readonly int[] options = new int[3];
    float mathTimer, prisonTime, failTimer, resultTimer;
    int dialogStep, dialogScore; bool dialogSwap; string lastReply, resultText;

    static readonly string[] DialogQ =
    {
        "Ну здравствуй, святоша. По какой статье заехал?",
        "Без лицензии... Уважаю. А стилеты у тебя для красоты или по понятиям?",
        "Последний вопрос. Кто в Дикоземье главный?"
    };
    static readonly string[] DialogGood =
    {
        "Изгонял демонов без лицензии.",
        "Дать подержать? Остриём вперёд.",
        "Шури. Но пока в ней сидит Белзе-Велзе — ты, Мурзик."
    };
    static readonly string[] DialogBad =
    {
        "Я тут случайно, начальник.",
        "Они освящённые. Потрогать — одна монетка.",
        "Ну... кошки?"
    };
    static readonly string[] ReactGood = { "Ха! Наш человек.", "...Понял, понял. Убери.", "Правильно базаришь. Мурр." };
    static readonly string[] ReactBad = { "Случайно тут только мыши, святоша.", "Ты мне ещё индульгенцию продай.", "Кошки?! Я тебе не кошка. Я — кот в законе." };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<ProtoGame>() != null) return;
        new GameObject("ProtoGame").AddComponent<ProtoGame>();
    }

    void Start()
    {
        Application.targetFrameRate = 120;
        var tex = new Texture2D(4, 4);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        tex.SetPixels(px); tex.Apply(); tex.filterMode = FilterMode.Point;
        square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);

        cam = Camera.main;
        if (cam == null) { cam = new GameObject("Camera").AddComponent<Camera>(); cam.tag = "MainCamera"; }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.07f, 0.1f);
        cam.transform.rotation = Quaternion.identity;

        logPath = Path.Combine(Application.dataPath, "..", "playtest-log.txt");
        Log("SESSION_START | build=vertical-scroller-v3");
        EnterHub();
    }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        switch (mode)
        {
            case Mode.Hub: UpdateHub(dt); break;
            case Mode.Run: UpdateRun(dt); break;
            case Mode.RunEnd: UpdateRunEnd(dt); break;
            case Mode.PrisonMath: UpdatePrisonMath(dt); break;
            case Mode.PrisonFail: UpdatePrisonFail(dt); break;
            case Mode.PrisonDialog: UpdatePrisonDialog(dt); break;
            case Mode.PrisonResult: UpdatePrisonResult(dt); break;
        }
    }

    // ---------- helpers ----------

    GameObject Quad(Vector2 pos, Vector2 size, Color c, int order)
    {
        var go = new GameObject("q");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = square; sr.color = c; sr.sortingOrder = order;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        modeObjects.Add(go);
        return go;
    }

    void ClearMode()
    {
        foreach (var g in modeObjects) if (g) Destroy(g);
        modeObjects.Clear(); enemies.Clear(); bullets.Clear(); enemyShots.Clear(); particles.Clear(); whipFx.Clear(); scrollDecor.Clear();
        player = null; tiriona = null; portal = null;
    }

    void Log(string s)
    {
        File.AppendAllText(logPath, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + s + "\n");
    }

    void SpawnPlayer(Vector2 at)
    {
        playerPos = at;
        player = Quad(at, new Vector2(0.8f, 0.8f), PlayerColor, 10);
        playerSr = player.GetComponent<SpriteRenderer>();
    }

    float MoveSpeed => 7.5f * (effect == Effect.DebuffSpeed ? 0.8f : 1f);

    Vector2 ReadMove()
    {
        var v = Vector2.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1;
        return v.sqrMagnitude > 1f ? v.normalized : v;
    }

    int ReadPick(int count)
    {
        int pick = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) pick = 0;
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) pick = 1;
        if (count > 2 && (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))) pick = 2;
        if (guiPick >= 0) { pick = guiPick; guiPick = -1; }
        return pick;
    }

    string EffectName(Effect e)
    {
        switch (e)
        {
            case Effect.BuffWhip: return "Благословение пахана: хлыст перезаряжается вдвое быстрее";
            case Effect.DebuffSpeed: return "Мытьё мисок: −20% к скорости";
            case Effect.DebuffHp: return "Не та шконка: −1 к макс. здоровью";
            default: return "нет";
        }
    }

    // ---------- hub ----------

    void EnterHub()
    {
        ClearMode(); mode = Mode.Hub; hubTime = 0; talkTimer = 0; talkedThisVisit = false;
        cam.orthographicSize = 6.5f; cam.transform.position = new Vector3(0, 0, -10);
        Quad(Vector2.zero, new Vector2(18, 11), new Color(0.22f, 0.17f, 0.12f), 0);
        Quad(new Vector2(0, 4.3f), new Vector2(6, 1.3f), new Color(0.45f, 0.3f, 0.15f), 1);
        tiriona = Quad(new Vector2(-5, 1), new Vector2(0.9f, 1.3f), new Color(0.65f, 0.3f, 0.85f), 5);
        portal = Quad(new Vector2(6, 0), new Vector2(1.4f, 2.2f), new Color(0.3f, 0.9f, 1f), 5);
        SpawnPlayer(new Vector2(0, -2.5f));
        Log("HUB_ENTER | last=" + lastOutcome + " | effect=" + effect);
    }

    string TirionaLine()
    {
        switch (lastOutcome)
        {
            case "buff": return "Слышала, ты подружился с местной... публикой. Полезные связи, Чемпион. Очень полезные.";
            case "debuff_dialog": return "Говорят, ты нахамил коту в законе. Смело. Глупо, но смело. Портал там.";
            case "debuff_math": return "Неправильно зашёл в хату? Орден бы тобой гордился. Наверное.";
            case "win": return "Ты выжил? Без засады? Хм. В смысле — браво! Я ни на что не намекала.";
            default: return "Чемпион! Портал ведёт в Дикоземье. Изгони демона из королевы Шури. Что может пойти не так?";
        }
    }

    void UpdateHub(float dt)
    {
        hubTime += dt;
        playerPos += ReadMove() * 6.5f * (effect == Effect.DebuffSpeed ? 0.8f : 1f) * dt;
        playerPos.x = Mathf.Clamp(playerPos.x, -8.5f, 8.5f);
        playerPos.y = Mathf.Clamp(playerPos.y, -5f, 5f);
        player.transform.position = playerPos;

        float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.08f;
        portal.transform.localScale = new Vector3(1.4f * pulse, 2.2f * pulse, 1f);

        if (talkTimer > 0) talkTimer -= dt;
        bool nearTiriona = Vector2.Distance(playerPos, tiriona.transform.position) < 2f;
        if (nearTiriona && (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return)))
        {
            talkLine = TirionaLine(); talkTimer = 5f;
            if (!talkedThisVisit) Log("HUB_TALK | t=" + hubTime.ToString("F1"));
            talkedThisVisit = true;
        }

        if (Vector2.Distance(playerPos, portal.transform.position) < 1.2f)
        {
            Log("HUB_PORTAL | hubTime=" + hubTime.ToString("F1") + " | talked=" + talkedThisVisit);
            StartRun();
        }
    }

    // ---------- run: vertical scroller ----------

    void StartRun()
    {
        ClearMode(); mode = Mode.Run; runIndex++; ambushRun = runIndex == 1;
        runTime = 0; waveTimer = 0.8f; trickleTimer = 0.2f; debrisTimer = 8f; kills = 0; shotsCancelled = 0; hitStop = 0; shake = 0;
        fireTimer = 0; whipCd = 0; prayerCd = 0; prayerTime = 0; invuln = 0;
        maxHp = effect == Effect.DebuffHp ? 4 : 5; hp = maxHp;
        cam.orthographicSize = field.y; cam.transform.position = new Vector3(0, 0, -10);

        Quad(Vector2.zero, field * 2f, new Color(0.16f, 0.2f, 0.13f), 0);
        var side = new Color(0.05f, 0.04f, 0.07f);
        Quad(new Vector2(field.x + 10f, 0), new Vector2(20f, field.y * 2 + 2), side, 30);
        Quad(new Vector2(-field.x - 10f, 0), new Vector2(20f, field.y * 2 + 2), side, 30);
        for (int i = 0; i < 45; i++)
        {
            var d = Quad(new Vector2(Random.Range(-field.x, field.x), Random.Range(-field.y, field.y)),
                         Vector2.one * Random.Range(0.25f, 0.9f), new Color(0.21f, 0.27f, 0.17f), 1);
            scrollDecor.Add(d);
        }

        SpawnPlayer(new Vector2(0, -field.y + 2f));
        Log("RUN_START | run=" + runIndex + " | effect=" + effect + " | ambush=" + ambushRun);
    }

    void UpdateRun(float dt)
    {
        if (hitStop > 0) { hitStop -= dt; UpdateCamera(dt); return; }
        runTime += dt;

        playerPos += ReadMove() * MoveSpeed * dt;
        playerPos.x = Mathf.Clamp(playerPos.x, -field.x + 0.4f, field.x - 0.4f);
        playerPos.y = Mathf.Clamp(playerPos.y, -field.y + 0.5f, field.y - 1.5f);
        player.transform.position = playerPos;

        fireTimer -= dt; whipCd -= dt; prayerCd -= dt; prayerTime -= dt; invuln -= dt;
        bool praying = prayerTime > 0;
        if (invuln > 0 && Mathf.Repeat(Time.time * 12f, 1f) < 0.5f) playerSr.color = new Color(1, 1, 1, 0.35f);
        else playerSr.color = praying ? PrayerColor : PlayerColor;

        bool fireHeld = Input.GetMouseButton(0) || Input.GetKey(KeyCode.J);
        if (fireHeld && fireTimer <= 0) { fireTimer = 0.085f; FireVolley(praying); }
        if ((Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.K)) && whipCd <= 0) { whipCd = effect == Effect.BuffWhip ? 1.25f : 2.5f; Whip(); }
        if ((Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.L)) && prayerCd <= 0)
        {
            prayerCd = 10f; prayerTime = 4f; shake = Mathf.Max(shake, 0.15f);
            Burst(playerPos, PrayerColor, 16, 7f);
        }

        UpdateSpawning(dt);
        UpdateScroll(dt);
        UpdateBullets(dt, praying);
        UpdateEnemies(dt);
        UpdateEnemyShots(dt);
        UpdateParticles(dt);
        UpdateWhipFx(dt);
        UpdateCamera(dt);

        if (hp <= 0) { EndRun("death", "Чемпиона повязали!", "prison"); return; }
        if (ambushRun && runTime >= 75f) { EndRun("ambush", "ЗАСАДА!\n\nТириона: «Ой. Кажется, я забыла упомянуть стражу...»", "prison"); return; }
        if (!ambushRun && runTime >= 90f) { EndRun("survived", "Дикоземье зачищено... на сегодня.\n\n(босса в прототипе нет)", "hub"); }
    }

    void UpdateScroll(float dt)
    {
        foreach (var d in scrollDecor)
        {
            var p = d.transform.position;
            p.y -= 4f * dt;
            if (p.y < -field.y - 1f) { p.y = field.y + 1f; p.x = Random.Range(-field.x, field.x); }
            d.transform.position = p;
        }
    }

    void UpdateSpawning(float dt)
    {
        float t01 = Mathf.Clamp01(runTime / 75f);

        trickleTimer -= dt;
        if (trickleTimer <= 0)
        {
            trickleTimer = Mathf.Lerp(0.6f, 0.2f, t01);
            SpawnEnemy(0, new Vector2(Random.Range(-field.x + 1, field.x - 1), field.y + 1f), Vector2.zero);
        }

        debrisTimer -= dt;
        if (debrisTimer <= 0)
        {
            debrisTimer = Mathf.Lerp(6f, 3f, t01);
            SpawnEnemy(4, new Vector2(Random.Range(-field.x + 1.5f, field.x - 1.5f), field.y + 1.5f), Vector2.zero);
        }

        waveTimer -= dt;
        if (waveTimer > 0) return;
        waveTimer = Mathf.Lerp(2.5f, 1.0f, t01);
        int patternCount = runTime > 30f ? 5 : (runTime > 5f ? 4 : 3);
        int pattern = Random.Range(0, patternCount);
        switch (pattern)
        {
            case 0: // horizontal line
            {
                int n = Random.Range(5, 9);
                for (int i = 0; i < n; i++)
                    SpawnEnemy(0, new Vector2(Mathf.Lerp(-field.x + 1, field.x - 1, n == 1 ? 0.5f : i / (float)(n - 1)), field.y + 1f), Vector2.zero);
                break;
            }
            case 1: // V formation
            {
                float cx = Random.Range(-field.x + 3, field.x - 3);
                for (int i = -3; i <= 3; i++)
                    SpawnEnemy(0, new Vector2(cx + i * 0.9f, field.y + 1f + Mathf.Abs(i) * 0.8f), Vector2.zero);
                break;
            }
            case 2: // side swoop
            {
                float dir = Random.value < 0.5f ? 1f : -1f;
                for (int i = 0; i < 6; i++)
                    SpawnEnemy(1, new Vector2(-dir * (field.x + 1f + i * 1.1f), field.y - 1f + i * 0.4f), new Vector2(dir * 5f, -2.5f));
                break;
            }
            case 3: // shooters
            {
                int n = runTime > 45f ? 3 : 2;
                for (int i = 0; i < n; i++)
                    SpawnEnemy(2, new Vector2(Random.Range(-field.x + 1.5f, field.x - 1.5f), field.y + 1f + i), Vector2.zero);
                break;
            }
            case 4: // censer
                SpawnEnemy(3, new Vector2(Random.Range(-field.x + 2f, field.x - 2f), field.y + 1.5f), Vector2.zero);
                break;
        }
    }

    static Color ColorFor(int kind)
    {
        switch (kind)
        {
            case 2: return ShooterColor;
            case 3: return CenserColor;
            case 4: return DebrisColor;
            default: return DemonColor;
        }
    }

    void SpawnEnemy(int kind, Vector2 p, Vector2 vel)
    {
        float hpFor = kind == 2 ? 6f : kind == 3 ? 12f : kind == 4 ? 18f : 2f;
        Vector2 size = kind == 2 ? new Vector2(1.1f, 1.1f) : kind == 3 ? new Vector2(1.4f, 1.4f) : kind == 4 ? new Vector2(2.4f, 1.2f) : new Vector2(0.75f, 0.75f);
        var e = new Enemy
        {
            kind = kind, pos = p, vel = vel, baseX = p.x, hp = hpFor, half = size * 0.5f,
            hoverY = kind == 3 ? Random.Range(4f, 6f) : Random.Range(3f, 6.5f),
            shootTimer = Random.Range(0.4f, 1.0f), dropShot = kind == 0 && Random.value < 0.2f
        };
        e.go = Quad(p, size, ColorFor(kind), kind == 4 ? 5 : 6);
        e.sr = e.go.GetComponent<SpriteRenderer>();
        enemies.Add(e);
    }

    void EnemyShoot(Vector2 from, Vector2 vel)
    {
        var s = new Bullet { pos = from, vel = vel, life = 7f };
        s.go = Quad(s.pos, Vector2.one * 0.4f, EnemyShotColor, 7);
        enemyShots.Add(s);
    }

    static bool Overlaps(Enemy e, Vector2 p, float r)
    {
        return Mathf.Abs(p.x - e.pos.x) < e.half.x + r && Mathf.Abs(p.y - e.pos.y) < e.half.y + r;
    }

    void FireVolley(bool praying)
    {
        if (praying) { FireBullet(-12f); FireBullet(0f); FireBullet(12f); }
        else FireBullet(Random.Range(-2.5f, 2.5f));
    }

    void FireBullet(float angleOffset)
    {
        Vector2 dir = Quaternion.Euler(0, 0, angleOffset) * Vector2.up;
        var b = new Bullet { pos = playerPos + Vector2.up * 0.5f, vel = dir * 22f, life = 1f };
        b.go = Quad(b.pos, new Vector2(0.15f, 0.6f), new Color(0.85f, 0.9f, 1f), 8);
        b.go.transform.rotation = Quaternion.Euler(0, 0, angleOffset);
        bullets.Add(b);
    }

    void UpdateBullets(float dt, bool praying)
    {
        float dmg = praying ? 1.5f : 1f;
        for (int i = bullets.Count - 1; i >= 0; i--)
        {
            var b = bullets[i];
            b.pos += b.vel * dt; b.life -= dt;
            bool dead = b.life <= 0 || b.pos.y > field.y + 1f;
            if (!dead)
            {
                foreach (var e in enemies)
                {
                    if (e.hp > 0 && Overlaps(e, b.pos, 0.12f)) { HitEnemy(e, dmg, Vector2.up * 2f); dead = true; break; }
                }
            }
            if (dead) { Destroy(b.go); bullets.RemoveAt(i); }
            else b.go.transform.position = b.pos;
        }
    }

    void Whip()
    {
        // frontal arc (upward); also cancels enemy shots inside it
        Vector2 fwd = Vector2.up;
        foreach (var e in enemies)
        {
            if (e.hp <= 0) continue;
            Vector2 to = e.pos - playerPos; float d = to.magnitude;
            if (d > 4.5f) continue;
            if (d < 1.2f || Vector2.Angle(fwd, to) <= 75f) HitEnemy(e, e.kind == 4 ? 6f : 3f, to.normalized * 9f);
        }
        for (int i = enemyShots.Count - 1; i >= 0; i--)
        {
            var s = enemyShots[i];
            Vector2 to = s.pos - playerPos; float d = to.magnitude;
            if (d > 4.5f || (d >= 1.2f && Vector2.Angle(fwd, to) > 75f)) continue;
            Burst(s.pos, EnemyShotColor, 4, 4f);
            Destroy(s.go); enemyShots.RemoveAt(i); shotsCancelled++;
        }
        for (int k = 0; k <= 10; k++)
        {
            float a = (90f - 75f + 15f * k) * Mathf.Deg2Rad;
            for (int r = 1; r <= 4; r++)
                whipFx.Add(Quad(playerPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * 1.1f), Vector2.one * 0.35f, new Color(1f, 0.9f, 0.6f, 0.9f), 9));
        }
        whipFxTime = 0.12f;
        shake = Mathf.Max(shake, 0.35f);
        hitStop = Mathf.Max(hitStop, 0.07f);
    }

    void UpdateWhipFx(float dt)
    {
        if (whipFx.Count == 0) return;
        whipFxTime -= dt;
        if (whipFxTime > 0) return;
        foreach (var g in whipFx) Destroy(g);
        whipFx.Clear();
    }

    void HitEnemy(Enemy e, float dmg, Vector2 knock)
    {
        bool heavy = e.kind >= 2;
        e.hp -= dmg; e.flash = 0.07f;
        if (e.kind != 4) e.push += knock;
        if (e.hp <= 0)
        {
            kills++;
            Burst(e.pos, ColorFor(e.kind), heavy ? 16 : 8, heavy ? 8f : 6f);
            shake = Mathf.Max(shake, heavy ? 0.22f : 0.08f);
            hitStop = Mathf.Max(hitStop, heavy ? 0.05f : 0.02f);
        }
    }

    void UpdateEnemies(float dt)
    {
        float descend = Mathf.Lerp(2.6f, 3.6f, Mathf.Clamp01(runTime / 75f));
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            bool offscreen = e.pos.y < -field.y - 2f || Mathf.Abs(e.pos.x) > field.x + 9f || ((e.kind == 2 || e.kind == 3) && e.pos.y > field.y + 4f && e.age > 2f);
            if (e.hp <= 0 || offscreen) { Destroy(e.go); enemies.RemoveAt(i); continue; }

            e.age += dt;
            switch (e.kind)
            {
                case 0:
                    e.pos.y -= descend * dt;
                    e.baseX += e.push.x * dt;
                    e.pos.x = e.baseX + Mathf.Sin(e.age * 2.2f + e.baseX) * 0.8f;
                    e.pos.y += e.push.y * dt;
                    if (e.dropShot && e.pos.y < 4f && e.pos.y > -2f)
                    {
                        e.dropShot = false;
                        EnemyShoot(e.pos, Vector2.down * 5f);
                    }
                    break;
                case 1:
                    e.pos += (e.vel + e.push) * dt;
                    break;
                case 2:
                    if (e.age > 8f) e.pos.y += 4f * dt;                  // leave upward
                    else if (e.pos.y > e.hoverY) e.pos.y -= 4f * dt;      // enter
                    e.pos.x += Mathf.Sin(e.age * 1.3f) * 1.2f * dt;
                    e.pos += e.push * dt * 0.3f;
                    e.shootTimer -= dt;
                    if (e.shootTimer <= 0 && e.age < 8f && e.pos.y < field.y)
                    {
                        e.shootTimer = 1.2f; e.shots++;
                        Vector2 aim = (playerPos - e.pos).normalized;
                        if (e.shots % 3 == 0)
                            for (int k = -1; k <= 1; k++) EnemyShoot(e.pos, (Vector2)(Quaternion.Euler(0, 0, k * 18f) * aim) * 4.5f);
                        else EnemyShoot(e.pos, aim * 4.8f);
                    }
                    break;
                case 3:
                    if (e.age > 10f) e.pos.y += 3f * dt;
                    else if (e.pos.y > e.hoverY) e.pos.y -= 2f * dt;
                    e.pos += e.push * dt * 0.2f;
                    e.shootTimer -= dt;
                    if (e.shootTimer <= 0 && e.age < 10f && e.pos.y < field.y)
                    {
                        e.shootTimer = 2.2f; e.shots++;
                        float off = e.shots * 15f;
                        for (int k = 0; k < 10; k++)
                        {
                            float a = (off + k * 36f) * Mathf.Deg2Rad;
                            EnemyShoot(e.pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.5f);
                        }
                        shake = Mathf.Max(shake, 0.1f);
                    }
                    break;
                case 4:
                    e.pos.y -= 4f * dt; // moves with the scrolling floor
                    break;
            }
            e.push = Vector2.Lerp(e.push, Vector2.zero, dt * 8f);
            e.go.transform.position = e.pos;
            e.flash -= dt;
            e.sr.color = e.flash > 0 ? Color.white : ColorFor(e.kind);

            if (Overlaps(e, playerPos, 0.32f)) DamagePlayer();
        }
    }

    void UpdateEnemyShots(float dt)
    {
        for (int i = enemyShots.Count - 1; i >= 0; i--)
        {
            var s = enemyShots[i];
            s.pos += s.vel * dt; s.life -= dt;
            bool dead = s.life <= 0 || Mathf.Abs(s.pos.x) > field.x + 1 || Mathf.Abs(s.pos.y) > field.y + 1;
            if (!dead && (s.pos - playerPos).sqrMagnitude < 0.36f) { DamagePlayer(); dead = true; }
            if (dead) { Destroy(s.go); enemyShots.RemoveAt(i); }
            else s.go.transform.position = s.pos;
        }
    }

    void DamagePlayer()
    {
        if (invuln > 0) return;
        hp--; invuln = 1f;
        shake = Mathf.Max(shake, 0.3f);
        hitStop = Mathf.Max(hitStop, 0.06f);
    }

    void Burst(Vector2 at, Color c, int n, float spd)
    {
        for (int i = 0; i < n; i++)
        {
            var p = new Particle { pos = at, vel = Random.insideUnitCircle.normalized * Random.Range(spd * 0.4f, spd), life = 0.35f, maxLife = 0.35f, color = c };
            p.go = Quad(at, Vector2.one * 0.22f, c, 7);
            p.sr = p.go.GetComponent<SpriteRenderer>();
            particles.Add(p);
        }
    }

    void UpdateParticles(float dt)
    {
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            p.life -= dt; p.pos += p.vel * dt; p.vel *= 0.9f;
            if (p.life <= 0) { Destroy(p.go); particles.RemoveAt(i); continue; }
            p.go.transform.position = p.pos;
            var c = p.color; c.a = p.life / p.maxLife; p.sr.color = c;
        }
    }

    void UpdateCamera(float dt)
    {
        shake = Mathf.Max(0f, shake - dt * 1.5f);
        Vector2 off = Random.insideUnitCircle * shake * 0.6f;
        cam.transform.position = new Vector3(off.x, off.y, -10f);
    }

    void EndRun(string outcome, string text, string next)
    {
        Log("RUN_END | run=" + runIndex + " | outcome=" + outcome + " | time=" + runTime.ToString("F1") + " | kills=" + kills + " | shotsCancelled=" + shotsCancelled + " | hp=" + hp);
        mode = Mode.RunEnd; endText = text; endTimer = 3f; endNext = next;
        effect = Effect.None; // the buff/debuff lasted exactly one run
        if (outcome == "survived") lastOutcome = "win";
    }

    void UpdateRunEnd(float dt)
    {
        UpdateParticles(dt);
        endTimer -= dt;
        if (endTimer <= 0 || (endTimer < 2f && Input.anyKeyDown))
        {
            if (endNext == "prison") EnterPrison(); else EnterHub();
        }
    }

    // ---------- prison ----------

    void EnterPrison()
    {
        ClearMode(); mode = Mode.PrisonMath; prisonTime = 0;
        cam.orthographicSize = 6.5f; cam.transform.position = new Vector3(0, 0, -10);
        Quad(Vector2.zero, new Vector2(18, 11), new Color(0.18f, 0.18f, 0.2f), 0);
        Quad(new Vector2(5, -2), new Vector2(2.2f, 2.6f), new Color(0.95f, 0.55f, 0.15f), 3); // Мурзик
        for (int i = -8; i <= 8; i += 2) Quad(new Vector2(i, 0), new Vector2(0.2f, 11), new Color(0.35f, 0.35f, 0.38f), 5);

        mathA = Random.Range(3, 10); mathB = Random.Range(3, 10); mathC = Random.Range(1, 10);
        int correct = mathA * mathB + mathC;
        correctIndex = Random.Range(0, 3);
        int[] deltas = { -10, -2, -1, 1, 2, 10, mathB, -mathB };
        for (int i = 0; i < 3; i++) options[i] = int.MinValue;
        options[correctIndex] = correct;
        for (int i = 0; i < 3; i++)
        {
            if (i == correctIndex) continue;
            int v;
            do { v = correct + deltas[Random.Range(0, deltas.Length)]; } while (System.Array.IndexOf(options, v) >= 0);
            options[i] = v;
        }
        mathTimer = 5f; guiPick = -1;
        Log("PRISON_ENTER");
    }

    void UpdatePrisonMath(float dt)
    {
        prisonTime += dt; mathTimer -= dt;
        int pick = ReadPick(3);
        if (pick < 0 && mathTimer > 0) return;
        bool ok = pick == correctIndex;
        Log("PRISON_MATH | ok=" + ok + " | timeLeft=" + Mathf.Max(0, mathTimer).ToString("F1"));
        if (ok) { mode = Mode.PrisonDialog; dialogStep = 0; dialogScore = 0; lastReply = null; dialogSwap = Random.value < 0.5f; }
        else { mode = Mode.PrisonFail; failTimer = 0; effect = Effect.DebuffHp; lastOutcome = "debuff_math"; }
    }

    void UpdatePrisonFail(float dt)
    {
        prisonTime += dt; failTimer += dt;
        if (failTimer > 4f || (failTimer > 1.5f && (Input.anyKeyDown || guiPick >= 0))) ExitPrison();
    }

    void UpdatePrisonDialog(float dt)
    {
        prisonTime += dt;
        int pick = ReadPick(2);
        if (pick < 0) return;
        bool good = pick == (dialogSwap ? 1 : 0);
        if (good) dialogScore++;
        lastReply = good ? ReactGood[dialogStep] : ReactBad[dialogStep];
        dialogStep++;
        dialogSwap = Random.value < 0.5f;
        if (dialogStep < DialogQ.Length) return;

        Log("PRISON_DIALOG | score=" + dialogScore + "/3");
        if (dialogScore >= 2)
        {
            effect = Effect.BuffWhip; lastOutcome = "buff";
            resultText = "Мурзик доволен.\n\n«" + lastReply + "»\n\nБлагословение пахана: хлыст перезаряжается вдвое быстрее (следующий забег).";
        }
        else
        {
            effect = Effect.DebuffSpeed; lastOutcome = "debuff_dialog";
            resultText = "Мурзик обиделся.\n\n«" + lastReply + "»\n\nТебя отправили мыть миски: −20% к скорости (следующий забег).";
        }
        mode = Mode.PrisonResult; resultTimer = 0;
    }

    void UpdatePrisonResult(float dt)
    {
        prisonTime += dt; resultTimer += dt;
        if (resultTimer > 1f && (Input.anyKeyDown || guiPick >= 0)) ExitPrison();
    }

    void ExitPrison()
    {
        guiPick = -1;
        Log("PRISON_EXIT | prisonTime=" + prisonTime.ToString("F1") + " | effect=" + effect);
        EnterHub();
    }

    // ---------- GUI ----------

    GUIStyle big, normal, button;

    void OnGUI()
    {
        if (cam == null) return;
        float scale = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        float w = Screen.width / scale, h = 1080f;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 48, alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
            normal = new GUIStyle(GUI.skin.label) { fontSize = 30, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 30, wordWrap = true };
        }

        switch (mode)
        {
            case Mode.Hub:
            {
                var c = new GUIStyle(normal) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, 40, w, 50), "ПРИГОВОР НА СЛЕДУЮЩИЙ ЗАБЕГ: " + EffectName(effect), c);
                GUI.Label(Centered(WorldToGui(tiriona.transform.position + Vector3.up * 1.2f, scale), 300, 40), "Тириона", c);
                GUI.Label(Centered(WorldToGui(portal.transform.position + Vector3.up * 1.6f, scale), 400, 40), "Портал в Дикоземье", c);
                if (Vector2.Distance(playerPos, tiriona.transform.position) < 2f)
                    GUI.Label(Centered(WorldToGui(tiriona.transform.position + Vector3.down * 1.2f, scale), 400, 40), "[E] поговорить", c);
                if (talkTimer > 0) GUI.Box(new Rect(w * 0.15f, h - 260, w * 0.7f, 150), "");
                if (talkTimer > 0) GUI.Label(new Rect(w * 0.15f + 20, h - 250, w * 0.7f - 40, 130), "Тириона: " + talkLine, normal);
                GUI.Label(new Rect(20, h - 60, w, 50), "WASD — ходить · E — говорить · войди в портал — начать забег", normal);
                break;
            }
            case Mode.Run:
            {
                // HUD lives in the dark side panels
                string whip = whipCd <= 0 ? "готов" : whipCd.ToString("F1") + "с";
                string pray = prayerTime > 0 ? "ДЕЙСТВУЕТ" : (prayerCd <= 0 ? "готова" : prayerCd.ToString("F0") + "с");
                float limit = ambushRun ? 75f : 90f;
                float panelW = Mathf.Max(260f, (w - h * field.x / field.y) / 2f - 30f);
                GUI.Label(new Rect(20, 30, panelW, 600),
                    "HP " + hp + "/" + maxHp + "\n\nУбито: " + kills + "\n\nВремя: " + runTime.ToString("F0") + " / " + limit.ToString("F0") +
                    "\n\nХлыст: " + whip + "\nМолитва: " + pray +
                    (effect != Effect.None ? "\n\nПриговор:\n" + EffectName(effect) : ""), normal);
                GUI.Label(new Rect(w - panelW - 20, 30, panelW, 600),
                    "WASD — движение\n\nЛКМ / J (зажать) — стилеты\n\nПКМ / K — хлыст (сбивает снаряды)\n\nПробел / L — молитва", normal);
                break;
            }
            case Mode.RunEnd:
                GUI.Label(new Rect(0, 0, w, h), endText, big);
                break;
            case Mode.PrisonMath:
            {
                GUI.Label(new Rect(0, 60, w, 80), "ТЮРЬМА ДИКОЗЕМЬЯ", big);
                GUI.Label(new Rect(w * 0.15f, 200, w * 0.7f, 160),
                    "Смотрящий у двери: «Правильно зайди в хату, святоша. Сколько будет " + mathA + " × " + mathB + " + " + mathC + "?»", normal);
                GUI.Label(new Rect(0, 360, w, 60), "Осталось: " + Mathf.Max(0, mathTimer).ToString("F1") + "с", new GUIStyle(big) { fontSize = 36 });
                for (int i = 0; i < 3; i++)
                    if (GUI.Button(new Rect(w * 0.5f - 450 + i * 310, 470, 280, 100), "[" + (i + 1) + "]  " + options[i], button)) guiPick = i;
                break;
            }
            case Mode.PrisonFail:
                GUI.Label(new Rect(0, 0, w, h), "Неправильно зашёл в хату.\n\nСпустя 1 срок...\n\n<size=30>Приговор на следующий забег: " + EffectName(effect) + "</size>", big);
                break;
            case Mode.PrisonDialog:
            {
                GUI.Label(new Rect(0, 60, w, 80), "Пахан Мурзик, кот в законе", big);
                float y = 200;
                if (lastReply != null) { GUI.Label(new Rect(w * 0.15f, y, w * 0.7f, 60), "Мурзик: «" + lastReply + "»", normal); y += 80; }
                GUI.Label(new Rect(w * 0.15f, y, w * 0.7f, 100), "Мурзик: «" + DialogQ[dialogStep] + "»", normal);
                string a = dialogSwap ? DialogBad[dialogStep] : DialogGood[dialogStep];
                string b = dialogSwap ? DialogGood[dialogStep] : DialogBad[dialogStep];
                if (GUI.Button(new Rect(w * 0.15f, y + 140, w * 0.7f, 90), "[1]  " + a, button)) guiPick = 0;
                if (GUI.Button(new Rect(w * 0.15f, y + 250, w * 0.7f, 90), "[2]  " + b, button)) guiPick = 1;
                break;
            }
            case Mode.PrisonResult:
                GUI.Label(new Rect(w * 0.1f, 0, w * 0.8f, h), resultText + (resultTimer > 1f ? "\n\n<size=26>(любая клавиша — выйти на волю)</size>" : ""), big);
                break;
        }
    }

    Vector2 WorldToGui(Vector3 world, float scale)
    {
        var s = cam.WorldToScreenPoint(world);
        return new Vector2(s.x / scale, (Screen.height - s.y) / scale);
    }

    static Rect Centered(Vector2 p, float width, float height) => new Rect(p.x - width / 2, p.y - height / 2, width, height);
}
