using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace archcartography.src
{
    public class GuiDialogChunksInMap : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "chunksinmapgui";

        public GuiDialogChunksInMap(ICoreClientAPI capi) : base(capi)
        {
            SetupDialog();
        }

        private void SetupDialog()
        {
            // Auto-sized dialog at the center of the screen
            ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            // Just a simple 300x300 pixel box
            ElementBounds textBounds = ElementBounds.Fixed(0, 40, 300, 100);

            // Background boundaries. Again, just make it fit it's child elements, then add the text as a child element
            ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(textBounds);

            // Lastly, create the dialog
            SingleComposer = capi.Gui.CreateCompo("guichunksinmap", dialogBounds)
                .AddShadedDialogBG(bgBounds)
                .AddDialogTitleBar("The Chunks in Map", OnTitleBarCloseClicked)
                .AddDynamicText("The count of chunks on map [" + ("no wand found?!").ToString() + "]",CairoFont.WhiteDetailText(),textBounds,"chunkCountText")
                .Compose()
            ;
        }

        private void OnTitleBarCloseClicked()
        {
            TryClose();
        }
    }

}
