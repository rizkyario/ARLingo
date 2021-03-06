﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CoreAnimation;
using CoreFoundation;
using CoreGraphics;
using CoreImage;
using CoreVideo;
using Foundation;
using Metal;
using SceneKit;
using UIKit;

namespace ARLingo
{
	public partial class ViewController : IUIPopoverPresentationControllerDelegate
	{
        public IVirtualObjectSelectionViewControllerDelegate Delegate { get; set; }

		static class SegueIdentifier
		{
			public static readonly NSString ShowSettings = new NSString("showSettings");
			public static readonly NSString ShowObjects = new NSString("showObjects");
		}

		[Export("adaptivePresentationStyleForPresentationController:")]
		public UIModalPresentationStyle GetAdaptivePresentationStyle(UIPresentationController forPresentationController)
		{
			return UIModalPresentationStyle.None;
		}

		[Export("adaptivePresentationStyleForPresentationController:traitCollection:")]
		public UIModalPresentationStyle GetAdaptivePresentationStyle(UIPresentationController controller, UITraitCollection traitCollection)
		{
			return UIModalPresentationStyle.None;
		}

		[Action("RestartExperience:")]
		public void RestartExperience(NSObject sender)
		{
            foreach (SCNNode label in Labels)
            {
                label.RemoveFromParentNode();
            }
            Labels.Clear();
			if (!RestartExperienceButton.Enabled || IsLoadingObject) 
			{
				return;
			}

			RestartExperienceButton.Enabled = false;

			UserFeedback.CancelAllScheduledMessages();
			UserFeedback.DismissPresentedAlert();
			UserFeedback.ShowMessage("STARTING A NEW SESSION");

			virtualObjectManager.RemoveAllVirtualObjects();
			AddObjectButton.SetImage(UIImage.FromBundle("add"), UIControlState.Normal);
			AddObjectButton.SetImage(UIImage.FromBundle("addPressed"), UIControlState.Highlighted);
			if (FocusSquare != null)
			{
				FocusSquare.Hidden = true;
			}
			ResetTracking();

			RestartExperienceButton.SetImage(UIImage.FromBundle("restart"), UIControlState.Normal);

			// Disable Restart button for a second in order to give the session enough time to restart.
			var when = new DispatchTime(DispatchTime.Now, new TimeSpan(0, 0, 1));
			DispatchQueue.MainQueue.DispatchAfter(when, () => SetupFocusSquare());
		}

        public void AddText(string Text, string Original)
        {
            if (Session == null || ViewController.CurrentFrame == null)
            {
                return;
            }
            var position = FocusSquare != null ? FocusSquare.LastPosition : new SCNVector3(0, 0, -1.5f);

            if (Text == null)
                return;
            var scnText = SCNText.Create(Text, 1);
            var txtMaterial = SCNMaterial.Create();
            var bckgndMaterial = SCNMaterial.Create();
            txtMaterial.Diffuse.Contents = UIColor.Red;
            scnText.FirstMaterial = txtMaterial;

            SCNVector3 min = new SCNVector3();
            SCNVector3 max = new SCNVector3();
            scnText.GetBoundingBox(ref min, ref max);
            var scnNode = SCNNode.Create();
            Labels.Add(scnNode);
            scnNode.Position = position;
            SCNVector3 campos = Session.CurrentFrame.Camera.Transform.Translation();
            SCNVector3 camnodevec = new SCNVector3(scnNode.Position - campos);
            scnNode.Look(campos + 2 * camnodevec);
            scnNode.Pivot = SCNMatrix4.CreateTranslation((max.X - min.X) / 2, 0, 0);
            scnNode.Scale = new SCNVector3(0.005f, 0.005f, 0.015f);
            scnNode.Geometry = scnText;

            SceneView.Scene.RootNode.AddChildNode(scnNode);

            if (AppSettings.ScaleWithPinchGesture == true)
            {
                var scnText2 = SCNText.Create(Original, 1);
                var txtMaterial2 = SCNMaterial.Create();
                txtMaterial2.Diffuse.Contents = UIColor.Blue;
                scnText2.FirstMaterial = txtMaterial2;

                SCNVector3 min2 = new SCNVector3();
                SCNVector3 max2 = new SCNVector3();
                scnText2.GetBoundingBox(ref min2, ref max2);
                var scnNode2 = SCNNode.Create();
                Labels.Add(scnNode2);
                scnNode2.Position = position;
                SCNVector3 camnodevec2 = new SCNVector3(scnNode2.Position - campos);
                scnNode2.Look(campos + 2 * camnodevec2);
                scnNode2.Pivot = SCNMatrix4.CreateTranslation((max2.X - min2.X) / 2, -10, 0);
                scnNode2.Scale = new SCNVector3(0.005f, 0.005f, 0.015f);
                scnNode2.Geometry = scnText2;
                SceneView.Scene.RootNode.AddChildNode(scnNode2);
            }
        }

        void ClassifyImageAsync(UIImage img)
        {
            if (AppSettings.DragOnInfinitePlanes == true)
                model.SwitchToModel("VGG16");
            else
                model.SwitchToModel("SqueezeNet");
            model.PredictionsUpdated += (s, e) => ShowPrediction(e.Value);
            Task.Run(() => model.Classify(img));
        }

        void ShowPrediction(ImageDescriptionPrediction imageDescriptionPrediction)
        {
            var topFive = imageDescriptionPrediction.predictions.Take(1);
            foreach (var prediction in topFive)
            {
                var prob = prediction.Item1;
                var desc = prediction.Item2;
                string res;
                try
                {
                    TranslateDict.Label_EN_FR.TryGetValue(desc, out res);
                }
                catch (Exception ex)
                {
                    res = desc;
                    Debug.WriteLine(ex.Message);
                }
                AddText(res, desc);
                if (prob < 0.7)
                    UserFeedback.ScheduleMessage("CONFIDENT IS ONLY " + (prob * 100).ToString() +  "%", 7.5, MessageType.PlaneEstimation);
            }
        }

		public void ChooseObject(UIButton button)
		{
			// Abort if we are about to load another object to avoid concurrent modifications of the scene.
			if (IsLoadingObject)
			{
				return;
			}

			UserFeedback.CancelScheduledMessage(MessageType.ContentPlacement);
            var currentImage = Session.CurrentFrame.CapturedImage;

            CIImage cimage = CIImage.FromImageBuffer(currentImage);
            string debug = cimage.PixelBuffer.PixelFormatType.ToString();
            CIContext tempContext = CIContext.FromOptions(null);
            CGImage videoImage = tempContext.CreateCGImage(cimage, new CGRect(0, 0, currentImage.Width, currentImage.Height));
            UIImage image = UIImage.FromImage(videoImage);
            UIImage rotated = new UIImage(image.CGImage, 1.0f, UIImageOrientation.Right);
            ClassifyImageAsync(rotated);

			//PerformSegue(SegueIdentifier.ShowObjects, button);
		}

        [Action("chooseObject:")]
        public void ChooseLang(UIButton button)
        {
            PerformSegue(SegueIdentifier.ShowObjects, button);
        }

		public override void PrepareForSegue(UIStoryboardSegue segue, NSObject sender)
		{
			// All popover segues should be popovers even on iPhone.
			var popoverController = segue.DestinationViewController?.PopoverPresentationController;
			if (popoverController == null)
			{
				return;
			}
			var button = (UIButton)sender;
			popoverController.Delegate = this;
			popoverController.SourceRect = button.Bounds;
			var identifier = segue.Identifier;
			if (identifier == SegueIdentifier.ShowObjects)
			{
				var objectsViewController = segue.DestinationViewController as VirtualObjectSelectionViewController;
				objectsViewController.Delegate = this;
			}
		}
	}
}
