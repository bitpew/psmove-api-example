using UnityEngine;
using System;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using io.thp.psmove;

public class PSMoveThread : MonoBehaviour {
	public static float FUSION_Z_NEAR = -1f;
	public static float FUSION_Z_FAR = 10f;

	public GameObject cursorObject;

	private bool useTracker = true;

	private Thread t;
	private bool running = true;

	private PSMoveCameraTracker cameraTracker;
	private LinkedList<PSMoveController> controllers = new LinkedList<PSMoveController>();

	float zNear;
	float zFar;

	void Start () {
		zNear = FUSION_Z_FAR;
		zFar = FUSION_Z_FAR;

		zNear = Camera.main.nearClipPlane;
		zFar = Camera.main.farClipPlane;

		zNear = 1;
		zFar = 10;

		t = new Thread(new ThreadStart(PSMoveHandler));
		t.Start();
		Debug.Log(zNear + ", " + zFar);
	}

	void Update() {

		Vector3? pos = null;
		Quaternion? r = null;

		//lock(controllers) {
			if(controllers.Count > 0) {
				var controller = controllers.First.Value;
				pos = controller.FusionPosition;
				r = controller.Orientation;
			/*
				if(controller.GetButton(PSMoveButton.Circle)) {
					psmoveapi_csharp.psmove_tracker_set_camera_color(cameraTracker.Tracker, controller.Move, 255, 0, 0);
					controller.SetColor(Color.red);
				}

				if(controller.GetButton(PSMoveButton.Triangle)) {
					psmoveapi_csharp.psmove_tracker_set_camera_color(cameraTracker.Tracker, controller.Move, 0, 0, 255);
					controller.SetColor(Color.blue);
				}
			*/
		}
		//}


		if(pos.HasValue) {
			Debug.Log(pos.Value.z);	
			if(cursorObject!=null) {
				//Vector3 screenPos = new Vector3( (pos.Value.x +1f)* (Screen.width/2), (-pos.Value.y + 1f) * (Screen.height/2), -pos.Value.z/*cursorObject.transform.position.z*/ );
				Vector3 screenPos = new Vector3( (pos.Value.x +1f)* (Screen.width/2), (-pos.Value.y + 1f) * (Screen.height/2), cursorObject.transform.position.z );
				Vector3 worldPos = Camera.main.ScreenToWorldPoint(screenPos);
				//worldPos.z = -pos.Value.z;
				cursorObject.transform.position = worldPos;
				//cursorObject.transform.rotation = r.Value;
				cursorObject.transform.rotation = r.Value;
			}
		}
	}

	public void PSMoveHandler() {
		try {
			
			psmoveapi_csharp.psmove_init(PSMove_Version.PSMOVE_CURRENT_VERSION);
			
			int numConnected = psmoveapi_csharp.count_connected();
			Log(string.Format("{0} controllers found", numConnected));
	
			if(useTracker && !SetupTracker()) {
				return;
			}

			Log("Creating Controller");
			var c = new PSMoveController(psmoveapi_csharp.psmove_connect());
			c.InitOrientation();
			c.EnableRateLimiting();

			if(!AddController(c)) {
				Log("Unable to create controller");
				c.Disconnect();
				c = null;
			} else {
				Log("Created controller");
				//c.SetColor(Color.green);
			}

			while (running) {
				//lock(controllers) {
					if(useTracker) {
						// update positions
						cameraTracker.Update();
						foreach(var controller in controllers) {
							cameraTracker.UpdateControllerPosition(controller);
						}
					}

					// update buttons and orientation
					foreach(var controller in controllers) {
						controller.Update();
					}
				//}

				//Thread.Sleep(20);
				/* Optional and not required by default
            byte r, g, b;
            tracker.get_color(move, out r, out g, out b);
            move.set_leds(r, g, b);
            move.update_leds();
            */
			}

		} catch(Exception e) {
			Log("Error while processing move controller(s): " + e.Message);
		}
	}

	private bool AddController(PSMoveController controller) {
		if(useTracker) {
			// Calibrate the controller in the camera picture
			int trackerEnableCount = 3;
			Log("Attempting to enable tracker");
			
			while (!cameraTracker.EnableTracker(controller)) {
				Log(string.Format("Failed to enable tracker with attempt {0}, status {1}", trackerEnableCount, cameraTracker.GetStatus(controller)));
				trackerEnableCount++;
				if(trackerEnableCount>1) {
					Log ("Permanently failed to enable tracker, exiting");
					return false;
				}
				//Thread.Sleep(1000);
			};

		}

		//lock(controllers) {
			controllers.AddLast(controller);
			//cameraTracker.EnableAutoUpdateColor(controller);
		//}

		return true;
	}

	private bool SetupTracker() {
		
		Log("Creating Tracker");
		cameraTracker = new PSMoveCameraTracker();
		Log("Tracker created");


		PSMove move = psmoveapi_csharp.psmove_tracker_exposure_lock_init();
		
		if(move == null) {
			Log("No Move Controller found, quitting");
			return false;
		}

		Func<bool> waitForMoveButton = () => {
			while(psmoveapi_csharp.psmove_poll(move) > 0) {
				uint buttons = psmoveapi_csharp.psmove_get_buttons(move);
				return (buttons & ( (uint)Button.Btn_MOVE) ) > 0;
			}
			return false;
		};

		Log("Press PSMove Button");
		while(!waitForMoveButton()) {
			Thread.Sleep(100);
			if(!running) return false;
		}
		
		Log("Exposure Lock Process");
		psmoveapi_csharp.psmove_tracker_exposure_lock_process(move, cameraTracker.Tracker, 0);
		
		Log("Press PSMove Button");
		while(!waitForMoveButton()) {
			Thread.Sleep(100);
			if(!running) return false;
		}
		psmoveapi_csharp.psmove_tracker_exposure_lock_finish(move);

		Log("Tracker enabled");

		//cameraTracker.EnableFusion(FUSION_Z_NEAR, FUSION_Z_FAR);
		cameraTracker.EnableFusion(zNear, zFar);

		return true;
	}
	
	private void CleanUp() {
		Log ("CleanUp");

		foreach(var controller in controllers) {
			if(cameraTracker.IsTracking(controller)) {
				cameraTracker.StopTracking(controller);
			}

			controller.Disconnect();	
		}

		controllers.Clear();
		cameraTracker.Disable();
	}

	private void Log(String message) {
		Debug.Log(message);
	} 
	
	void OnApplicationQuit() {
		running = false;
		//t.Join();
		CleanUp();
	}
}
