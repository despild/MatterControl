﻿/*
Copyright (c) 2014, Kevin Pope
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.PlatformAbstract;
using MatterHackers.Agg.UI;
using MatterHackers.VectorMath;

using MatterHackers.Localizations;
using MatterHackers.MatterControl.PrinterCommunication;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.MatterControl.CustomWidgets;

namespace MatterHackers.MatterControl.PrinterControls
{
    public class FanControls : ControlWidgetBase
    {

		TextWidget fanToggleSwitchText;
		ToggleSwitch toggleSwitch;

        protected override void AddChildElements()
        {
            AltGroupBox fanControlsGroupBox = new AltGroupBox(new TextWidget("Fan Controls".Localize(), pointSize: 18, textColor: ActiveTheme.Instance.SecondaryAccentColor));

            fanControlsGroupBox.Margin = new BorderDouble(0);            
            fanControlsGroupBox.BorderColor = ActiveTheme.Instance.PrimaryTextColor;
            fanControlsGroupBox.HAnchor |= Agg.UI.HAnchor.ParentLeftRight;
            fanControlsGroupBox.VAnchor = Agg.UI.VAnchor.FitToChildren;

            {
                FlowLayoutWidget fanControlsLayout = new FlowLayoutWidget(FlowDirection.TopToBottom);
                fanControlsLayout.HAnchor = Agg.UI.HAnchor.ParentLeftRight;
                fanControlsLayout.VAnchor = Agg.UI.VAnchor.FitToChildren;
                fanControlsLayout.Padding = new BorderDouble(3, 5, 3, 0) * TextWidget.GlobalPointSizeScaleRatio;
                {
                    fanControlsLayout.AddChild(CreateFanControls());
                }

                fanControlsGroupBox.AddChild(fanControlsLayout);
            }
            
            this.HAnchor = Agg.UI.HAnchor.ParentLeftRight;
            this.AddChild(fanControlsGroupBox);
        }


        
        private GuiWidget CreateFanControls()
        {
        //    PrinterConnectionAndCommunication.Instance.FanSpeedSet.RegisterEvent(FanSpeedChanged_Event, ref unregisterEvents);

            FlowLayoutWidget leftToRight = new FlowLayoutWidget();
            leftToRight.Padding = new BorderDouble(3, 0, 0, 5) * TextWidget.GlobalPointSizeScaleRatio;

            

			//Matt's test editing to add a on/off toggle switch
			bool fanActive = PrinterConnectionAndCommunication.Instance.FanSpeed0To255 != 0;

			fanToggleSwitchText = new TextWidget (fanActive?"On":"Off", pointSize: 10, textColor: ActiveTheme.Instance.PrimaryTextColor);
			fanToggleSwitchText.VAnchor = Agg.UI.VAnchor.ParentCenter;		


			toggleSwitch = toggleSwitchFactory.GenerateGivenTextWidget (fanToggleSwitchText,"On","Off", fanActive);		
			toggleSwitch.VAnchor = Agg.UI.VAnchor.ParentCenter;		
			toggleSwitch.valueChanged += new EventHandler (ToggleSwitch_Click);

			leftToRight.AddChild (fanToggleSwitchText);
			leftToRight.AddChild(new HorizontalSpacer());
			leftToRight.AddChild(toggleSwitch);

			leftToRight.Width = 200;

            return leftToRight;
        }
						
		void ToggleSwitch_Click(object sender, EventArgs e)
		{
			ToggleSwitch toggleSwitch = (ToggleSwitch)sender;
			if (toggleSwitch.getValue ()) 
			{
				PrinterConnectionAndCommunication.Instance.FanSpeed0To255 = 255;
			}
			else
			{
				PrinterConnectionAndCommunication.Instance.FanSpeed0To255 = 0;
			}
		}
    }
}
