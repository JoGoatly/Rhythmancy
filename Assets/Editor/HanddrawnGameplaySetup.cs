using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using RhythmWitchClone.Gameplay;
using RhythmWitchClone.UI;
using Object = UnityEngine.Object;

namespace RhythmWitchClone.EditorTools
{
    /// <summary>Rebuildable, serialized scene artwork. No dependency on the Downloads folder at runtime.</summary>
    public static class HanddrawnGameplaySetup
    {
        const string Art = "Assets/Art/Handdrawn/";
        static readonly string[] Scenes = { "Tutorial", "Tutorial Level", "SafeZone" };
        static Material cutout;
        static System.Random random;
        static float Next(float a, float b) => a + (float)random.NextDouble() * (b - a);
        static Sprite Sprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + path);
        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        static T Ref<T>(Object target, string field) where T : Object =>
            new SerializedObject(target).FindProperty(field).objectReferenceValue as T;
        static void Number(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Place(rect, anchor, position, size);
            return rect;
        }
        static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
        static Image Picture(string name, Transform parent, Sprite sprite, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var image = Rect(name, parent, anchor, position, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }
        static TMP_Text Label(string name, Transform parent, Vector2 position, Vector2 size, int fontSize)
        {
            var text = Rect(name, parent, Vector2.one * .5f, position, size).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = new Color(.23f, .19f, .13f);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        [MenuItem("Tools/Rhythmancy/Build Handdrawn Gameplay")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.Refresh();
            foreach (string file in Directory.GetFiles(Art, "*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".png") || p.EndsWith(".jpeg")))
            {
                var importer = AssetImporter.GetAtPath(file) as TextureImporter;
                if (importer == null) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            cutout = AssetDatabase.LoadAssetAtPath<Material>(Art + "Drawing.mat");
            if (cutout == null)
            {
                cutout = new Material(Shader.Find("Rhythmancy/PaperCutout"));
                AssetDatabase.CreateAsset(cutout, Art + "Drawing.mat");
            }
            var prefab = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            ConfigureHud(prefab.GetComponentInChildren<PlayerController>(true));
            PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Prefabs/Player.prefab");
            PrefabUtility.UnloadPrefabContents(prefab);
            foreach (string name in Scenes)
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                var player = Object.FindFirstObjectByType<PlayerController>(FindObjectsInactive.Include);
                if (player == null) throw new InvalidOperationException("Player missing in " + name);
                var instance = PrefabUtility.GetOutermostPrefabInstanceRoot(player.gameObject);
                if (instance != null) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                ConfigureHud(player);
                BuildMap(player);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Validate();
            Debug.Log("HANDDRAWN_BUILD_OK: HUD, eight keys, animated cards and 72 x 54 forest saved in all gameplay scenes.");
        }

        static void ConfigureHud(PlayerController player)
        {
            if (player == null) throw new InvalidOperationException("Missing player");
            var visual = Ref<Transform>(player, "visualTransform");
            if (visual != null) { visual.localScale = Vector3.one * 1.5f; visual.localPosition = new Vector3(0,.8f,0); }
            var lane = player.GetComponent<BeatLaneController>();
            var cards = player.GetComponentInChildren<CardAbilityController>(true);
            if (cards == null) cards = player.transform.root.GetComponentInChildren<CardAbilityController>(true);
            var laneRect = Ref<RectTransform>(lane, "laneContainer");
            var canvas = laneRect.GetComponentInParent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            Transform old = canvas.transform.Find("Handdrawn HUD");
            if (old != null) old.name = "Previous Handdrawn HUD";
            var hud = Rect("Handdrawn HUD", canvas.transform, Vector2.one * .5f, Vector2.zero, Vector2.zero);
            hud.anchorMin = Vector2.zero; hud.anchorMax = Vector2.one;
            hud.SetAsFirstSibling();

            var score = Picture("Score", hud, Sprite("HUD/Medaillon_Score-HUD.png"), new Vector2(0,1), new Vector2(190,-115), new Vector2(300,176));
            Picture("Medallion", score.transform, Sprite("HUD/Medaillon.png"), Vector2.one * .5f, new Vector2(-100,-43), new Vector2(48,44));
            var coin = Ref<TMP_Text>(player, "coinCountText");
            if (coin != null)
            {
                coin.transform.SetParent(score.transform, false);
                Place(coin.rectTransform, Vector2.one * .5f, new Vector2(15,-38), new Vector2(160,40));
                coin.fontSize = 26; coin.color = new Color(.23f,.19f,.13f);
            }
            var readout = hud.gameObject.AddComponent<GameplayHudReadout>();
            var wave = Picture("Wave", hud, Sprite("HUD/Wave-HUD.png"), new Vector2(.5f,1), new Vector2(-85,-107), new Vector2(154,158));
            var clock = Picture("Time", hud, Sprite("HUD/Time-HUD.png"), new Vector2(.5f,1), new Vector2(85,-107), new Vector2(154,158));
            readout.levelText = Label("Level", wave.transform, new Vector2(0,10), new Vector2(105,55), 32);
            readout.timeText = Label("Elapsed", clock.transform, new Vector2(0,10), new Vector2(110,55), 30);
            readout.levelText.text = "1"; readout.timeText.text = "00:00";

            var vitals = Rect("Instrument and Vitals", hud, new Vector2(.5f,0), new Vector2(-15,108), new Vector2(430,200));
            Picture("Wood Panel", vitals, Sprite("Musikobjekte/Musikobjekte/Textfeld.png"), Vector2.one * .5f, new Vector2(60,-24), new Vector2(350,135));
            Picture("Violin", vitals, Sprite("Musikobjekte/Musikobjekte/Streichinstrument.png"), Vector2.one * .5f, new Vector2(-110,40), new Vector2(190,250));
            MakeBar(Ref<Slider>(player,"healthSlider"), vitals, new Vector2(83,0), false);
            MakeBar(Ref<Slider>(cards,"manaBarSlider"), vitals, new Vector2(83,-47), true);
            // Retire the old sheet fragments after moving the live sliders and currency label.
            foreach (string name in new[] { "HpGImage", "HpNoteImage", "HpHeartImage", "HpBackGroundImage", "CoinIcon", "HealthText", "HpPercentText", "ManaPercentText" })
                foreach (var t in player.transform.root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name))
                    t.gameObject.SetActive(false);

            laneRect.SetParent(hud, false);
            Place(laneRect, Vector2.one * .5f, new Vector2(-255,156), new Vector2(330,74));
            laneRect.pivot = new Vector2(0,.5f);
            var laneImage = laneRect.GetComponent<Image>(); if (laneImage != null) laneImage.enabled = false;
            Number(lane,"noteIconSize",64);
            var hit = Ref<RectTransform>(lane,"hitZoneMarker");
            if (hit != null)
            {
                Place(hit,new Vector2(0,.5f),new Vector2(40,0),new Vector2(70,70));
                foreach (var graphic in hit.GetComponentsInChildren<Graphic>()) graphic.color = new Color(.9f,.85f,.55f,.18f);
            }
            var feedback = Ref<TMP_Text>(lane,"feedbackText");
            if (feedback != null) { feedback.transform.SetParent(hud,false); Place(feedback.rectTransform,Vector2.one*.5f,new Vector2(0,115),new Vector2(300,45)); feedback.fontSize=24; }
            var laneData = new SerializedObject(lane);
            var keys = laneData.FindProperty("laneKeys"); keys.arraySize = 8;
            string letters = "HIZUJKLO";
            for (int i=0;i<8;i++)
            {
                var key = keys.GetArrayElementAtIndex(i);
                string letter = letters[i].ToString();
                key.FindPropertyRelative("requiredCharacter").intValue = letters[i];
                key.FindPropertyRelative("icon").objectReferenceValue = Sprite("Buchstabenkacheln/"+letter+".png");
                key.FindPropertyRelative("auraColor").colorValue = Color.HSVToRGB(i/8f,.4f,.85f);
                string notesPath = "Assets/Visuals/Notes/"+letter+"_notes.png";
                key.FindPropertyRelative("noteSpriteSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(notesPath);
                var notes = AssetDatabase.LoadAllAssetsAtPath(notesPath).OfType<Sprite>().ToArray();
                var array = key.FindPropertyRelative("noteAuraImages"); array.arraySize=notes.Length;
                for(int n=0;n<notes.Length;n++) array.GetArrayElementAtIndex(n).objectReferenceValue=notes[n];
            }
            laneData.FindProperty("visibleNoteCount").intValue = 4;
            laneData.ApplyModifiedPropertiesWithoutUndo();

            var fan = Rect("Sheet Music",hud,new Vector2(1,0),new Vector2(-320,220),new Vector2(500,370));
            var fields = new[]{"defCardTransform","atkCardTransform","spcCardTransform"};
            var sprites = new[]{"DEF","ATK","SPC"};
            for(int i=0;i<3;i++)
            {
                var card = Ref<RectTransform>(cards,fields[i]);
                card.SetParent(fan,false);
                Place(card,Vector2.one*.5f,new Vector2((i-1)*112,i==1?22:0),new Vector2(238,305));
                card.localRotation=Quaternion.Euler(0,0,i==0?16:i==2?-16:0);
                var image=card.GetComponent<Image>(); image.sprite=Sprite("HUD/"+sprites[i]+"_Boost-HUD.png"); image.color=Color.white; image.preserveAspect=true; image.raycastTarget=true;
                foreach(var text in card.GetComponentsInChildren<TMP_Text>(true))
                { text.gameObject.SetActive(false); }
            }
            var status=Label("Special feedback",fan,new Vector2(0,315),new Vector2(520,100),24);
            status.text="";
            Set(cards,"specialFeedbackText",status);
            Ref<RectTransform>(cards,"atkCardTransform").SetAsLastSibling();
            Number(cards,"cardLiftDistance",38); Number(cards,"cardLiftSpeed",10); Number(cards,"specialCardPulseDuration",.65f);
            var stack = player.transform.root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="CardStack");
            if(stack!=null) { var image=stack.GetComponent<Image>(); if(image!=null) image.enabled=false; }
            if (old != null) Object.DestroyImmediate(old.gameObject);
            EditorUtility.SetDirty(player); EditorUtility.SetDirty(cards); EditorUtility.SetDirty(lane);
        }
        static void MakeBar(Slider slider, Transform parent, Vector2 pos, bool mana)
        {
            if(slider==null) return;
            slider.transform.SetParent(parent,false);
            slider.gameObject.SetActive(true);
            foreach (string generated in new[] { "Ink outline", "Bar interior" })
            { var old = slider.transform.Find(generated); if (old != null) Object.DestroyImmediate(old.gameObject); }
            Place((RectTransform)slider.transform,Vector2.one*.5f,pos,new Vector2(210,16));
            foreach(Transform child in slider.transform) child.gameObject.SetActive(false);
            slider.fillRect=null; slider.handleRect=null;
            var display=Picture("Bar interior",slider.transform,null,Vector2.one*.5f,Vector2.zero,new Vector2(210,20));
            display.preserveAspect=false;
            var bar=slider.GetComponent<IllustratedResourceBar>();
            if(bar==null) bar=slider.gameObject.AddComponent<IllustratedResourceBar>();
            bar.display=display;
            string[] health={"Rechts-01-Leer","Links-13-Rot","Links-11-Rot","Links-09-Gruen","Links-07-Gruen","Links-05-Gruen","Links-03-Gruen","Links-01-Gruen","Rechts-15-Gruen","Rechts-13-Gruen","Rechts-11-Gruen","Rechts-09-Gruen","Rechts-07-Gruen","Rechts-05-Gruen","Rechts-03-Gruen"};
            string[] magic={"Rechts-02-Leer","Links-14-Blau","Links-12-Blau","Links-10-Blau","Links-08-Blau","Links-06-Blau","Links-04-Blau","Links-02-Blau","Rechts-16-Blau","Rechts-14-Blau","Rechts-12-Blau","Rechts-10-Blau","Rechts-08-Blau","Rechts-06-Blau","Rechts-04-Blau"};
            bar.stages=(mana?magic:health).Select(n=>Sprite("Musikobjekte/Musikobjekte/"+n+".png")).ToArray();
            bar.levels=new[]{0f,.05f,.1f,.2f,.3f,.4f,.45f,.5f,.65f,.75f,.8f,.85f,.9f,.95f,1f};
            slider.targetGraphic=display; slider.interactable=false; slider.transition=Selectable.Transition.None;
            bar.Refresh();

        }

        static void BuildMap(PlayerController player)
        {
            random=new System.Random(1138);
            var old=GameObject.Find("Handdrawn Forest Map"); if(old!=null) Object.DestroyImmediate(old);
            var root=new GameObject("Handdrawn Forest Map").transform;
            Vector3 center=new Vector3(player.transform.position.x,0,player.transform.position.z);
            var camera=player.transform.root.GetComponentInChildren<Camera>(true);
            if(camera==null) camera=Camera.main;
            camera.orthographic=true; camera.orthographicSize=7.8f;
            camera.transform.rotation=Quaternion.Euler(55,0,0);
            camera.transform.position=center + new Vector3(0,17,-11.9f);
            camera.backgroundColor=new Color(.43f,.53f,.26f);
            var follow=camera.GetComponent<CameraFollow>(); if(follow!=null) Set(follow,"target",player.transform);
            var ground=GameObject.Find("Ground");
            if(ground!=null) ground.SetActive(false);
            var path=GameObject.Find("Path"); if(path!=null && path.GetComponent<Renderer>()!=null) path.GetComponent<Renderer>().enabled=false;
            var floor=new GameObject("Walkable ground"); floor.transform.SetParent(root,false);
            floor.transform.position=center+Vector3.down*.15f;
            var collider=floor.AddComponent<BoxCollider>(); collider.size=new Vector3(72,.3f,54);
            Sprite grass=Sprite("Map-Assets/Map-Background.jpeg");
            for(int x=0;x<6;x++) for(int z=0;z<10;z++)
            {
                var tile=new GameObject($"Grass {x:D2}-{z:D2}"); tile.transform.SetParent(root,false);
                tile.transform.position=center+new Vector3(-30+x*12,.005f,-24.3f+z*5.4f);
                tile.transform.rotation=Quaternion.Euler(90,0,0);
                tile.transform.localScale=new Vector3(12.01f/grass.bounds.size.x,5.41f/grass.bounds.size.y,1);
                var renderer=tile.AddComponent<SpriteRenderer>(); renderer.sprite=grass; renderer.sharedMaterial=DrawingMaterial(grass.texture);
                renderer.sharedMaterial.shader=Shader.Find("Rhythmancy/PaperGround");
            }
            // Continuous collision boundary sits inside the double tree belt.
            Wall(root,center+new Vector3(-33,2,0),new Vector3(1,4,54));
            Wall(root,center+new Vector3(33,2,0),new Vector3(1,4,54));
            Wall(root,center+new Vector3(0,2,-22),new Vector3(66,4,1));
            Wall(root,center+new Vector3(0,2,24),new Vector3(66,4,1));
            for(int row=0;row<2;row++)
            {
                for(float x=-36;x<=36;x+=2.4f)
                {
                    Prop(root,"Baum",center+new Vector3(x+Next(-.4f,.4f),0,-25+row*1.8f+Next(-.3f,.3f)),Next(3.5f,4.8f),camera.transform.rotation,true);
                    Prop(root,"Baum",center+new Vector3(x+Next(-.4f,.4f),0,26-row*1.8f+Next(-.3f,.3f)),Next(3.5f,4.8f),camera.transform.rotation,true);
                }
                for(float z=-22;z<24;z+=2.4f)
                {
                    Prop(root,"Baum",center+new Vector3(-35+row*1.8f+Next(-.3f,.3f),0,z),Next(3.5f,4.8f),camera.transform.rotation,true);
                    Prop(root,"Baum",center+new Vector3(35-row*1.8f+Next(-.3f,.3f),0,z),Next(3.5f,4.8f),camera.transform.rotation,true);
                }
            }
            Physics.SyncTransforms();
            string[] props={"Busch01","Busch2","Stein01","Stein2","NotenStein","Blume"};
            var occupied=new System.Collections.Generic.List<Vector3>();
            for(int i=0;i<140;i++)
            {
                Vector3 point=center+new Vector3(Next(-29,29),0,Next(-19,21));
                Vector3 local=point-center;
                // Open central clearing and two broad, gently curving routes.
                if(local.magnitude<9 || Mathf.Abs(local.x-3*Mathf.Sin(local.z*.13f))<3 || Mathf.Abs(local.z)<2.5f) continue;
                if(occupied.Any(p=>Vector3.Distance(p,point)<2.2f)) continue;
                if(Physics.OverlapSphere(point+Vector3.up,1.5f).Any(c=>c.transform.root!=root && !c.isTrigger)) continue;
                occupied.Add(point);
                string kind=props[i%props.Length];
                Prop(root,kind,point,kind=="Blume"?Next(.35f,.65f):Next(.65f,1.3f),camera.transform.rotation,false);
                if(i%7==0) Prop(root,"Baum",point+new Vector3(1.3f,0,1),Next(3,4.2f),camera.transform.rotation,true);
            }
            // Break up the joins with irregular, non-blocking plant/stone clusters.
            for(int x=1;x<6;x++) for(int z=1;z<10;z++)
            {
                Vector3 junction=center+new Vector3(-36+x*12,0,-27+z*5.4f);
                if(Vector3.Distance(junction,center)<5) continue;
                for(int j=0;j<3;j++)
                {
                    Vector3 point=junction+new Vector3(Next(-1.3f,1.3f),0,Next(-.8f,.8f));
                    Vector3 local=point-center;
                    if(Mathf.Abs(local.z)<2.5f || Mathf.Abs(local.x-3*Mathf.Sin(local.z*.13f))<3) continue;
                    Prop(root,j==0?"Busch2":j==1?"Stein2":"Blume",point,j==0?Next(.8f,1.3f):Next(.35f,.65f),camera.transform.rotation,false);
                }
            }

        }
        static Material DrawingMaterial(Texture texture)
        {
            string path=Art+"Drawing-"+texture.name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null)
            {
                material=new Material(Shader.Find("Rhythmancy/PaperCutout"));
                AssetDatabase.CreateAsset(material,path);
            }
            material.mainTexture=texture; EditorUtility.SetDirty(material);
            return material;
        }
        static void Wall(Transform root,Vector3 position,Vector3 size)
        {
            var wall=new GameObject("Forest boundary"); wall.transform.SetParent(root,false); wall.transform.position=position;
            wall.AddComponent<BoxCollider>().size=size;
        }
        static void Prop(Transform root,string name,Vector3 position,float height,Quaternion rotation,bool solid)
        {
            var sprite=Sprite("Map-Assets/"+name+".png");
            if(sprite==null) throw new InvalidOperationException("Missing map sprite: "+name);
            var prop=new GameObject(name); prop.transform.SetParent(root,false); prop.transform.position=position;
            var visual=new GameObject("Drawing"); visual.transform.SetParent(prop.transform,false);
            float scale=height/sprite.bounds.size.y;
            visual.transform.rotation=rotation; visual.transform.localScale=Vector3.one*scale;
            visual.transform.position=position+rotation*Vector3.up*(-sprite.bounds.min.y*scale);
            var renderer=visual.AddComponent<SpriteRenderer>(); renderer.sprite=sprite; renderer.sharedMaterial=DrawingMaterial(sprite.texture); renderer.flipX=random.Next(2)==0;
            if(solid) { var trunk=prop.AddComponent<CapsuleCollider>(); trunk.radius=.3f; trunk.height=2; trunk.center=Vector3.up; }
        }
        // Capture from serialized scenes without saving preview-only state or touching save slots.
        public static void CapturePreview()
        {
            Validate();
            EditorSceneManager.OpenScene("Assets/Scenes/Tutorial Level.unity");
            var player = Object.FindFirstObjectByType<PlayerController>();
            var lane = player.GetComponent<BeatLaneController>();
            var cards = player.GetComponent<CardAbilityController>();
            var canvas = Ref<RectTransform>(lane, "laneContainer").GetComponentInParent<Canvas>();
            foreach (Transform child in canvas.transform) child.gameObject.SetActive(child.name == "Handdrawn HUD");
            var camera = Camera.main;
            foreach (var billboard in Object.FindObjectsByType<BillboardSprite>(FindObjectsSortMode.None))
            { Invoke(billboard, "Awake"); Invoke(billboard, "LateUpdate"); }
            var rt = new RenderTexture(1920, 1080, 24);
            rt.Create(); camera.targetTexture = rt;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            Invoke(lane, "Start"); Invoke(lane, "UpdateNotePositionsAndAnimation");
            if (Ref<RectTransform>(lane,"laneContainer").GetComponentsInChildren<Image>().Count(i=>i.name.StartsWith("Note_"))!=4) throw new Exception("Four visible notes required");
            Invoke(cards, "Start");
            Ref<Slider>(player,"healthSlider").normalizedValue=1;
            Ref<Slider>(cards,"manaBarSlider").normalizedValue=1;
            foreach (var slider in new[] { Ref<Slider>(player,"healthSlider"), Ref<Slider>(cards,"manaBarSlider") })
            {
                var resourceBar=slider.GetComponent<IllustratedResourceBar>();
                for(int i=0;i<resourceBar.levels.Length;i++)
                {
                    slider.normalizedValue=resourceBar.levels[i];
                    resourceBar.Refresh();
                    if(resourceBar.display.sprite==null || resourceBar.display.sprite!=resourceBar.stages[i])
                        throw new Exception("Incorrect resource sprite at stage "+i);
                }
                slider.normalizedValue=.5f;
                slider.GetComponent<IllustratedResourceBar>().Refresh();
                if(slider.GetComponent<IllustratedResourceBar>().display.sprite != slider.GetComponent<IllustratedResourceBar>().stages[7])
                    throw new Exception("Illustrated bar does not follow slider value");
                slider.normalizedValue=1;
                slider.GetComponent<IllustratedResourceBar>().Refresh();
                if(slider.GetComponent<IllustratedResourceBar>().display.sprite != slider.GetComponent<IllustratedResourceBar>().stages[14]) throw new Exception("Full resource sprite missing");
            }
            Directory.CreateDirectory("Captures");
            Capture(camera, rt, "Captures/gameplay-hud.png");
            // Verify that selection really changes the front card, including the special pulse.
            Invoke(cards,"ToggleCard",0); Invoke(cards,"UpdateCardVisuals",.5f);
            AssertFront(cards,"defCardTransform");
            Capture(camera, rt, "Captures/gameplay-def.png");
            Invoke(cards,"ToggleCard",1); Invoke(cards,"UpdateCardVisuals",.5f);
            AssertFront(cards,"atkCardTransform");
            Capture(camera, rt, "Captures/gameplay-atk.png");
            var manaField=typeof(CardAbilityController).GetField("_currentMana",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            float maxMana=new SerializedObject(cards).FindProperty("maxMana").floatValue;
            manaField.SetValue(cards,maxMana*.99f);
            Invoke(cards,"TriggerSpecialCard");
            if(string.IsNullOrEmpty(Ref<TMP_Text>(cards,"specialFeedbackText").text) || Mathf.Abs((float)manaField.GetValue(cards)-maxMana*.99f)>.001f)
                throw new Exception("SPC must explain insufficient mana without consuming it");
            manaField.SetValue(cards,maxMana);
            Invoke(cards,"TriggerSpecialCard");
            if((float)manaField.GetValue(cards)!=0f || GameObject.Find("AuraRing")==null)
                throw new Exception("SPC did not consume full mana and create its shockwave");
            Invoke(cards,"UpdateCardVisuals",.5f); AssertFront(cards,"spcCardTransform");
            Invoke(cards,"UpdateManaBarUI");
            Ref<Slider>(cards,"manaBarSlider").GetComponent<IllustratedResourceBar>().Refresh();
            Capture(camera, rt, "Captures/gameplay-spc.png");
            canvas.gameObject.SetActive(false);
            Vector3 center = player.transform.position; center.y=0;
            camera.transform.position=center+new Vector3(0,55,-38.5f); camera.orthographicSize=29;
            Capture(camera,rt,"Captures/forest-map.png");
            camera.targetTexture=null; rt.Release(); Object.DestroyImmediate(rt);
            File.WriteAllText("Captures/gameplay-tests.txt", "PASS: eight assigned letters in all three scenes; four visible notes; DEF/ATK/SPC foreground switching, enlargement and preserved fan angles; illustrated HP/mana at 50% and 100%; SPC rejection feedback at 99% and actual shockwave creation/mana consumption at 100%; serialized map and HUD. Editor preview checks, not a full Play Mode test.\n");
            Debug.Log("HANDDRAWN_PREVIEW_OK: foreground selection and scene rendering checked.");
        }
        static void AssertFront(CardAbilityController cards,string field)
        {
            var card=Ref<RectTransform>(cards,field);
            if(card.GetSiblingIndex()!=card.parent.childCount-1) throw new Exception("Card not in foreground: "+field);
            if(card.localScale.x < 1.17f) throw new Exception("Selected card not enlarged: "+field);
            float expectedAngle = field == "defCardTransform" ? 16 : field == "spcCardTransform" ? -16 : 0;
            if(Quaternion.Angle(card.localRotation, Quaternion.Euler(0,0,expectedAngle)) > .1f)
                throw new Exception("Card fan angle changed: "+field);
        }
        static void Invoke(Object target,string method,params object[] args)
        {
            target.GetType().GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(target,args);
        }
        static void Capture(Camera camera,RenderTexture rt,string path)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            camera.Render();
            RenderTexture.active=rt;
            var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
            File.WriteAllBytes(path,image.EncodeToPNG());
            RenderTexture.active=null; Object.DestroyImmediate(image);
        }
        [MenuItem("Tools/Rhythmancy/Validate Handdrawn Gameplay")]
        public static void Validate()
        {
            foreach(string name in Scenes)
            {
                var scene=EditorSceneManager.OpenScene("Assets/Scenes/"+name+".unity");
                var player=Object.FindFirstObjectByType<PlayerController>();
                var lane=player.GetComponent<BeatLaneController>();
                var keys=new SerializedObject(lane).FindProperty("laneKeys");
                if(keys.arraySize!=8) throw new Exception(name+": eight keys required");
                for(int i=0;i<8;i++) if(keys.GetArrayElementAtIndex(i).FindPropertyRelative("icon").objectReferenceValue==null) throw new Exception(name+": missing key sprite");
                if(GameObject.Find("Handdrawn Forest Map")==null) throw new Exception(name+": map missing");
                if(Object.FindFirstObjectByType<GameplayHudReadout>()==null) throw new Exception(name+": HUD missing");
                Debug.Log("HANDDRAWN_VALIDATED: "+scene.name);
            }
        }
    }
}
