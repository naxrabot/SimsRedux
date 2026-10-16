using System;
using System.Collections.Generic;
using System.Linq;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Client.UI.Controls;
using Microsoft.Xna.Framework;
using FSO.SimAntics;
using FSO.HIT;
using FSO.Vitaboy;
using FSO.Common.Rendering.Framework.Camera;
using FSO.Common.Rendering.Framework;
using FSO.Common.Utils;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.Common;
using FSO.Client;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.Utils;
using Simitone.Client.UI.Controls;

namespace Simitone.Client.UI.Panels
{
    public class UIPieMenu : UIContainer
    {
        public UIPieMenuItem m_PieTree;
        public List<UIButton> m_PieButtons;
        public UIPieMenuItem m_CurrentItem;
        public VMEntity m_Obj;
        public VMEntity m_Caller;
        public UILotControl m_Parent;
        public UIImage m_Bg;

        private Vector2 currentTarget;
        private Vector2 curRot;
        private float lerpSpeed;

        private _3DTargetScene HeadScene;
        private BasicCamera HeadCamera;
        private double m_BgGrow;
        private float TrueScale;

        private bool ShiftDown; //shift activates IDE

        //This is a standard AdultVitaboyModel instance. Since nothing is needed but the head for pie menus,
        //the other parts of the body will be stripped from it (see constructor).
        private SimAvatar m_Head;

        private TextStyle ButtonStyle;
        private TextStyle HighlightStyle;

        // SimsRedux: classic Sims 1 pie menu (a soft dark shadow behind the head, every choice on a
        // dark blue rounded tag with white words, like the original "Go Here")
        public static bool Classic
        {
            get { return UIInteraction.Classic; }
        }
        private Texture2D ClassicBack;
        private static Texture2D ClearButton;
        private List<Vector2> Balls = new List<Vector2>();
        private const float Radius = 62f, BallGap = 2f;

        private static Texture2D ShadowTex;

        /// <summary>The soft dark shadow the original draws around the head (darkest in the middle).</summary>
        private static Texture2D SoftShadow()
        {
            if (ShadowTex != null && !ShadowTex.IsDisposed) return ShadowTex;
            const int size = 128;
            var data = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy);
                    float a = (r >= 1) ? 0 : (float)Math.Pow(1 - r, 0.9) * 0.62f;
                    data[y * size + x] = new Color(0, 0, 0) * a;
                }
            ShadowTex = new Texture2D(GameFacade.GraphicsDevice, size, size);
            ShadowTex.SetData(data);
            return ShadowTex;
        }

        /// <summary>
        /// Classic: a dark blue tag with rounded ends (drawn by the button as left end, stretched
        /// middle, right end), like the original's "Go Here".
        /// </summary>
        private Texture2D ButtonTexture()
        {
            if (!Classic) return TextureGenerator.GetPieButtonImg(GameFacade.GraphicsDevice);
            if (ClearButton == null || ClearButton.IsDisposed)
            {
                const int third = 9, w = third * 3, h = 30, rad = 8;
                var fill = new Color(14, 34, 132);
                var edge = new Color(70, 100, 200);
                var data = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        // distance from the rounded rectangle's edge (negative = inside)
                        float qx = Math.Max(rad, Math.Min(w - 1 - rad, x));
                        float qy = Math.Max(rad, Math.Min(h - 1 - rad, y));
                        float d = (float)Math.Sqrt((x - qx) * (x - qx) + (y - qy) * (y - qy)) - rad;
                        Color c;
                        if (d > 0.5f) c = Color.Transparent;
                        else if (d > -1.5f) c = edge * Math.Min(1f, 0.5f - d); // soft lighter rim
                        else c = fill;
                        data[y * w + x] = c;
                    }
                ClearButton = new Texture2D(GameFacade.GraphicsDevice, w, h);
                ClearButton.SetData(data);
            }
            return ClearButton;
        }

        public UIPieMenu(List<VMPieMenuInteraction> pie, VMEntity obj, VMEntity caller, UILotControl parent)
        {
            if (FSOEnvironment.UIZoomFactor > 1.33f) ScaleX = ScaleY = FSOEnvironment.UIZoomFactor * 0.75f;
            TrueScale = ScaleX * FSOEnvironment.DPIScaleFactor;
            m_PieButtons = new List<UIButton>();
            this.m_Obj = obj;
            this.m_Caller = caller;
            this.m_Parent = parent;
            this.ButtonStyle = new TextStyle
            {
                Font = GameFacade.MainFont,
                VFont = GameFacade.VectorFont,
                Size = 12,
                Color = new Color(0xA5, 0xC3, 0xD6),
                SelectedColor = new Color(0x00, 0xFF, 0xFF),
                CursorColor = new Color(255, 255, 255)
            };

            HighlightStyle = ButtonStyle.Clone();
            HighlightStyle.Color = Color.Yellow;

            if (Classic)
            {
                // SimsRedux: the Sims 1 look: white words on dark blue rounded tags, yellow under the mouse
                ButtonStyle.Color = Color.White;
                ButtonStyle.HighlightedColor = Color.Yellow;
                ButtonStyle.SelectedColor = Color.Yellow;
                ButtonStyle.Shadow = true;
                ButtonStyle.Size = 15;
                HighlightStyle.Size = 15;
                HighlightStyle.Shadow = true;
                HighlightStyle.HighlightedColor = Color.White;
                ClassicBack = SoftShadow();
                // classic interface size on big screens
                var uiScale = Math.Max(1f, (float)Math.Floor(GlobalSettings.Default.GraphicsHeight / 768f * 4) / 4f);
                if (uiScale > ScaleX) { ScaleX = ScaleY = uiScale; TrueScale = ScaleX * FSOEnvironment.DPIScaleFactor; }
            }

            lerpSpeed = 0.125f * (60.0f / FSOEnvironment.RefreshRate);
            m_Bg = new UIImage((Classic && ClassicBack != null) ? ClassicBack : TextureGenerator.GetPieBG(GameFacade.GraphicsDevice));
            m_Bg.SetSize(2, 2); //is scaled up later
            m_Bg.Position = new Vector2(-1, -1);
            this.AddAt(0, m_Bg);

            m_PieTree = new UIPieMenuItem()
            {
                Category = true
            };

            for (int i = 0; i < pie.Count; i++)
            {
                string[] depth = (pie[i].Name == null) ? new string[] { "???" } : pie[i].Name.Split('/');

                var category = m_PieTree; //set category to root
                for (int j = 0; j < depth.Length - 1; j++) //iterate through categories
                {
                    if (category.ChildrenByName.ContainsKey(depth[j]))
                    {
                        category = category.ChildrenByName[depth[j]];
                    }
                    else
                    {
                        var newCat = new UIPieMenuItem()
                        {
                            Category = true,
                            Name = depth[j],
                            Parent = category
                        };
                        category.Children.Add(newCat);
                        category.ChildrenByName[depth[j]] = newCat;
                        category = newCat;
                    }
                }
                //we are in the category, put the interaction in here;

                var name = depth[depth.Length - 1];
                var semiInd = name.LastIndexOf(';');
                int colorMod = 0;
                if (semiInd > -1)
                {
                    int.TryParse(name.Substring(semiInd + 1), out colorMod);
                    name = name.Substring(0, semiInd);
                }

                var item = new UIPieMenuItem()
                {
                    Category = false,
                    Name = name,
                    ColorMod = colorMod,
                    ID = pie[i].ID,
                    Param0 = pie[i].Param0,
                    Global = pie[i].Global
                };
                category.Children.Add(item);
                category.ChildrenByName[item.Name] = item;
            }

            m_CurrentItem = m_PieTree;
            m_PieButtons = new List<UIButton>();
            RenderMenu();

            VMAvatar Avatar = (VMAvatar)caller;
            m_Head = new SimAvatar(Avatar.Avatar); //talk about confusing...
            m_Head.StripAllButHead();

            initSimHead();
        }

        private void initSimHead()
        {
            HeadCamera = new BasicCamera(GameFacade.GraphicsDevice, new Vector3(0.0f, 7.0f, -17.0f), Vector3.Zero, Vector3.Up);

            var pos2 = m_Head.Skeleton.GetBone("HEAD").AbsolutePosition;

            HeadCamera.Position = new Vector3(0, pos2.Y, 12.5f);
            HeadCamera.Target = pos2;

            HeadScene = new _3DTargetScene(GameFacade.GraphicsDevice, HeadCamera, new Point((int)(200 * TrueScale), (int)(200 * TrueScale)), (GlobalSettings.Default.AntiAlias > 0) ? 8 : 0);
            HeadScene.ID = "UIPieMenuHead";

            m_Head.Scene = HeadScene;
            m_Head.Scale = new Vector3(1f);

            HeadCamera.Zoom = 0f;
            HeadScene.Add(m_Head);
            GameFacade.Scenes.AddExternal(HeadScene); //AddExternal(HeadScene);
        }

        public void RotateHeadCam(Vector2 point)
        {
            curRot = Vector2.Lerp(curRot, currentTarget, lerpSpeed);
            double xdir = Math.Atan(-curRot.X / 100.0);
            double ydir = Math.Atan(-curRot.Y / 100.0);

            Vector3 off = new Vector3(0, 0, 13.5f);
            Matrix mat = Microsoft.Xna.Framework.Matrix.CreateRotationY((float)xdir) * Microsoft.Xna.Framework.Matrix.CreateRotationX((float)ydir);

            HeadCamera.Position = new Vector3(0, 5.2f, 0) + Vector3.Transform(off, mat);
        }

        private bool SimSceneRemoved;
        public void RemoveSimScene()
        {
            if (SimSceneRemoved || HeadScene == null) return;
            SimSceneRemoved = true;
            GameFacade.Scenes.RemoveExternal(HeadScene);
            HeadScene.Target.Dispose();
        }

        /// <summary>SimsRedux: a pie menu still open when the lot closes lets go of its head picture too.</summary>
        public override void Removed()
        {
            base.Removed();
            RemoveSimScene();
        }

        public void UpdateHeadPosition(int x, int y)
        {
            HeadCamera.ProjectionOrigin = new Vector2(100, 100);
        }

        public override void Update(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            base.Update(state);
            if (m_BgGrow < 1)
            {
                m_BgGrow += 1.0 / 30.0 * (60.0 / FSOEnvironment.RefreshRate);
                HeadCamera.Zoom = (float)m_BgGrow * 5.12f;

                float bgSize = Classic ? 300 : 200; // the classic shadow spreads wider
                m_Bg.SetSize((float)m_BgGrow * bgSize, (float)m_BgGrow * bgSize);
                m_Bg.X = (float)m_BgGrow * (-bgSize / 2);
                m_Bg.Y = (float)m_BgGrow * (-bgSize / 2);
            }
            RotateHeadCam(GlobalPoint(new Vector2(state.MouseState.X, state.MouseState.Y)));
            ShiftDown = state.ShiftDown;
        }

        public void RenderMenu()
        {
            for (int i = 0; i < m_PieButtons.Count; i++) //remove previous buttons
            {
                this.Remove(m_PieButtons[i]);
            }
            m_PieButtons.Clear();
            Balls.Clear();

            var elems = m_CurrentItem.Children;
            int dirConfig;
            if (elems.Count > 4) dirConfig = 8;
            else if (elems.Count > 2) dirConfig = 4;
            else dirConfig = 2;

            for (int i = 0; i < dirConfig; i++)
            {
                if (i >= elems.Count) break;
                var elem = elems.ElementAt(i);
                var but = new UIButton()
                {
                    // TS1 does ellipsis on categories manually.
                    Caption = elem.Name, // + ((elem.Category) ? "..." : ""),
                    CaptionStyle = (elem.ColorMod > 0) ? HighlightStyle : ButtonStyle,
                    ImageStates = 1,
                    Texture = ButtonTexture()
                };

                double dir = (((double)i) / dirConfig) * Math.PI * 2;
                but.AutoMargins = Classic ? 9 : 4;
                if (Classic)
                {
                    var ball = new Vector2((float)(Math.Sin(dir) * Radius), (float)(Math.Cos(dir) * -Radius));
                    Balls.Add(ball);
                    PlaceClassic(but, ball, i, dirConfig);
                    this.Add(but);
                    m_PieButtons.Add(but);
                    but.OnButtonClick += new ButtonClickDelegate(PieButtonClick);
                    but.OnButtonHover += new ButtonClickDelegate(PieButtonHover);
                    but.OnButtonExit += new ButtonClickDelegate(PieButtonExit);
                    continue;
                }

                if (i == 0)
                { //top
                    but.X = (float)(Math.Sin(dir) * 60 - but.Width / 2);
                    but.Y = (float)((Math.Cos(dir) * -60) - but.Size.Y);
                }
                else if (i == dirConfig / 2)
                { //bottom
                    but.X = (float)(Math.Sin(dir) * 60 - but.Width / 2);
                    but.Y = (float)((Math.Cos(dir) * -60));
                }
                else if (i < dirConfig / 2) //on right side
                {
                    but.X = (float)(Math.Sin(dir) * 60);
                    but.Y = (float)((Math.Cos(dir) * -60) - but.Size.Y / 2);
                }
                else //on left side
                {
                    but.X = (float)(Math.Sin(dir) * 60 - but.Width);
                    but.Y = (float)((Math.Cos(dir) * -60) - but.Size.Y / 2);
                }

                this.Add(but);
                m_PieButtons.Add(but);
                but.OnButtonClick += new ButtonClickDelegate(PieButtonClick);
                but.OnButtonHover += new ButtonClickDelegate(PieButtonHover);
                but.OnButtonExit += new ButtonClickDelegate(PieButtonExit);
            }

            bool top = true;
            for (int i = 8; i < elems.Count; i++)
            {
                var elem = elems.ElementAt(i);
                var but = new UIButton()
                {
                    Caption = elem.Name + ((elem.Category) ? "..." : ""),
                    CaptionStyle = (elem.ColorMod > 0) ? HighlightStyle : ButtonStyle,
                    ImageStates = 1,
                    Texture = ButtonTexture()
                };
                but.AutoMargins = Classic ? 9 : 4;

                but.X = (float)(-but.Width / 2);
                if (top)
                { //top
                    but.Y = (float)(-60 - but.Size.Y * ((i - 8) / 2 + 2));
                }
                else
                {
                    but.Y = (float)(60 + but.Size.Y * ((i - 8) / 2 + 1));
                }

                this.Add(but);
                m_PieButtons.Add(but);
                but.OnButtonClick += new ButtonClickDelegate(PieButtonClick);
                but.OnButtonHover += new ButtonClickDelegate(PieButtonHover);
                but.OnButtonExit += new ButtonClickDelegate(PieButtonExit);
                top = !top;
            }

            if (m_CurrentItem.Parent != null)
            {
                var but = new UIButton()
                {
                    Caption = m_CurrentItem.Name,
                    CaptionStyle = ButtonStyle.Clone(),
                    ImageStates = 1,
                    Texture = ButtonTexture()
                };

                but.CaptionStyle.Color = but.CaptionStyle.SelectedColor;
                but.AutoMargins = Classic ? 9 : 4;
                but.X = (float)(-but.Width / 2);
                but.Y = (float)(-but.Size.Y / 2);
                this.Add(but);
                m_PieButtons.Add(but);
                but.OnButtonClick += new ButtonClickDelegate(BackButtonPress);
            }
        }

        /// <summary>Classic: the words sit outside the ball (above, below, left or right of it).</summary>
        private void PlaceClassic(UIButton but, Vector2 ball, int i, int dirConfig)
        {
            float h = but.Size.Y;
            if (i == 0) { but.X = ball.X - but.Width / 2; but.Y = ball.Y - BallGap - h; }
            else if (i == dirConfig / 2) { but.X = ball.X - but.Width / 2; but.Y = ball.Y + BallGap; }
            else if (i < dirConfig / 2) { but.X = ball.X + BallGap; but.Y = ball.Y - h / 2; }
            else { but.X = ball.X - BallGap - but.Width; but.Y = ball.Y - h / 2; }
        }

        void PieButtonHover(UIElement button)
        {
            var uiB = (UIButton)button;
            int index = m_PieButtons.IndexOf(uiB);
            currentTarget = button.Position + new Vector2(uiB.Width / 2f, uiB.Size.Y / 2f);
            HITVM.Get().PlaySoundEvent(UISounds.PieMenuHighlight);
        }

        void PieButtonExit(UIElement button)
        {
            currentTarget = Vector2.Zero;
        }

        void BackButtonPress(UIElement button)
        {
            if (m_CurrentItem.Parent == null) return; //shouldn't ever be...
            m_CurrentItem = m_CurrentItem.Parent;
            HITVM.Get().PlaySoundEvent(UISounds.PieMenuSelect);
            RenderMenu();
        }

        private void PieButtonClick(UIElement button)
        {
            int index = m_PieButtons.IndexOf((UIButton)button);
            if (index == -1) return; //bail! this isn't meant to happen!
            var action = m_CurrentItem.Children.ElementAt(index);
            HITVM.Get().PlaySoundEvent(UISounds.PieMenuSelect);

            if (action.Category)
            {
                m_CurrentItem = action;
                RenderMenu();
            }
            else
            {

                if (m_Obj == m_Parent.GotoObject)
                {
                    m_Parent.vm.SendCommand(new VMNetGotoCmd
                    {
                        Interaction = action.ID,
                        Param0 = action.Param0,
                        ActorUID = m_Caller.PersistID,
                        x = m_Obj.Position.x,
                        y = m_Obj.Position.y,
                        level = m_Obj.Position.Level
                    });
                }
                else
                {
                    if (FSO.Client.Debug.IDEHook.IDE != null && ShiftDown)
                    {
                        if (m_Obj.TreeTable.InteractionByIndex.ContainsKey((uint)action.ID))
                        {
                            var act = m_Obj.TreeTable.InteractionByIndex[(uint)action.ID];
                            ushort ActionID = act.ActionFunction;

                            var function = m_Obj.GetBHAVWithOwner(ActionID, m_Parent.vm.Context);

                            FSO.Client.Debug.IDEHook.IDE.IDEOpenBHAV(
                                function.bhav,
                                m_Obj.Object
                            );
                        }
                    }
                    else
                    {
                        m_Parent.vm.SendCommand(new VMNetInteractionCmd
                        {
                            Interaction = action.ID,
                            ActorUID = m_Caller.PersistID,
                            CalleeID = m_Obj.ObjectID,
                            Param0 = action.Param0,
                            Global = action.Global
                        });
                    }
                }
                HITVM.Get().PlaySoundEvent(UISounds.QueueAdd);
                m_Parent.ClosePie();

            }
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            HeadScene.Draw(GameFacade.GraphicsDevice);
            base.PreDraw(batch);
        }

        public override void Draw(UISpriteBatch batch)
        {
            base.Draw(batch);

            if (m_CurrentItem == m_PieTree)
            {
                var invScale = new Vector2(1 / TrueScale, 1 / TrueScale);
                DrawLocalTexture(batch, HeadScene.Target, null, new Vector2(-100, -100), invScale);
            } //if we're top level, draw head!
        }
    }

    public class UIPieMenuItem
    {
        public bool Category;
        public byte ID;
        public short Param0;
        public bool Global;
        public int ColorMod;
        public string Name;
        public List<UIPieMenuItem> Children = new List<UIPieMenuItem>();
        public Dictionary<string, UIPieMenuItem> ChildrenByName = new Dictionary<string, UIPieMenuItem>();
        public UIPieMenuItem Parent;
    }
}
