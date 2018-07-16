using System;
using System.Collections.Generic;
using UnityEngine;
using VectorHelpers;

public class ModuleScienceExperimentCamera : ModuleScienceExperiment
{
    public enum CameraType
    {
        photo,
    };

    public enum FilmType
    {
        blackAndWhite,
        color
    };

    [KSPField]
    string displayMaterialName = "display";

    [KSPField]
    string cameraTransformName = "camera";

    [KSPField]
    public Vector2 photoSize = new Vector2(400, 400);

    [KSPField]
    public Vector2 panoramaSize = new Vector2(800, 400);

    [KSPField]
    public Vector2 panoramaAngles = new Vector2(180, 90);

    [UI_FloatRange(minValue = 10f, maxValue = 150f, stepIncrement = 1f)]
    [KSPField(isPersistant = true, guiName = "Field of view", guiActiveEditor = true, guiActive = true)]
    public float fieldOfView = -1;

    //[KSPField(isPersistant = false, guiName = "Camera type", guiActiveEditor = true, guiActive = true)]
    //[UI_Label()]
    [KSPField]
    public CameraType cameraType = CameraType.photo;

    [KSPField]
    public FilmType filmType = FilmType.color;

    [KSPField(isPersistant = false, guiName = "Debug camera", guiActiveEditor = true, guiActive = true, advancedTweakable = true)]
    [UI_Toggle(disabledText = "Disabled", enabledText = "Enabled")]
    public bool debug = false;

    [KSPField(isPersistant = false, guiName = "Display", guiActiveEditor = false, guiActive = true)]
    [UI_Toggle(enabledText = "Active", disabledText = "Inactive")]
    public bool displayActive = false;
    public bool displayWasActive = false;

    [KSPField(isPersistant = false, guiName = "Status", guiActiveEditor = false, guiActive = true)]
    [UI_Label()]
    public string status;

    [KSPField(isPersistant = false, guiName = "Progress", guiActiveEditor = false, guiActive = true)]
    [UI_ProgressBar(minValue = 0, maxValue = 100)]
    public float progress = 0;

    RenderTexture renderTexture;
    Photo photo;

    LineInfo lineForward;
    LineInfo lineForwardProjected;
    LineInfo lineUp;
    LineInfo lineEast;
    LineInfo lineReference;
    // The direction of the first shot
    Vector3 reference;

    bool snap;
    private BaseEvent experimentAction;
    private PopupDialog dialog;
    private DialogGUILabel dialogStatus;
    private Camera camera;

    bool isPanorama;

    [KSPEvent(name = "SnapPhoto", guiName = "Take a photo", active = true, guiActive = true, requireFullControl = false)]
    public void SnapPhoto()
    {
        if (debug)
            printf("SnapPhoto");

        if (!displayActive)
        {
            displayActive = true;
            UpdateDisplay();
            displayActive = false;
        }
        snap = true;
    }

    [KSPEvent(name = "ViewPhoto", guiName = "View photo", active = true, guiActive = true, requireFullControl = false)]
    public void ViewPhoto()
    {

        Vector2 size;
        Texture composite = photo.GetComposite();
        if (composite.width > 720)
        {
            size.x = 720;
            size.y = 720 * composite.height / composite.width;
        }
        else
        {
            size.x = composite.width;
            size.y = composite.height;
        }

        MultiOptionDialog multiOptionDialog =
            new MultiOptionDialog(
                "Popup",
                "This is gonna look great!",
                isPanorama ? "View panorama" : "View photo",
                HighLogic.UISkin,
                size.x + 20,
                new DialogGUIBase[]
                {
                    new DialogGUIImage( size,
                        new Vector2(0.0f, 0.0f), Color.white, composite),
                    dialogStatus = new DialogGUILabel("Not enough data"),
                    new DialogGUIButton("Prepare transmission", delegate { TransmitData(); }, false),
                    new DialogGUIButton("Close", delegate { dialog.Dismiss(); }, true)
                    // new DialogGUIFlexibleSpace(),
                });
        dialog = PopupDialog.SpawnPopupDialog(multiOptionDialog,
            false,
            HighLogic.UISkin,
            false);
        dialog.SetDraggable(true);

        SetStatus(status);
    }

    class Photo
    {
        bool[] snapped;
        Texture2D composite;
        Vector2 panoramaAngle;

        public Photo(Vector2 size, Vector2 panoramaAngle)
        {
            snapped = new bool[(int)size.x];
            composite = new Texture2D((int)size.x, (int)size.y, TextureFormat.RGB24, false);
            this.panoramaAngle = panoramaAngle;
            Clear();
            //composite.Create();
        }

        public void Clear()
        {
            for (int i = 0; i < snapped.Length; i++)
                snapped[i] = false;

            for (int y = 0; y < composite.height; y++)
                for (int x = 0; x < composite.width; x++)
                    composite.SetPixel(x, y, Color.black);
            composite.Apply();
        }

        public string Snap(Vector2 angle, Texture2D texture, bool debug)
        {
            int heightRange = composite.height - texture.height;
            /*
            if (angle.y < -panoramaAngle.y / 2)
                angle.y = -panoramaAngle.y / 2;
            if (angle.y > panoramaAngle.y / 2)
                angle.y = panoramaAngle.y / 2;
            */
            int left = (int)((angle.x + panoramaAngle.x / 2) * composite.width / panoramaAngle.x - texture.width / 2);
            if (left < 0)
                left += composite.width;

            int top = 0;
            if (texture.height != panoramaAngle.y)
            {
                if (angle.y < -panoramaAngle.y / 2)
                    return "Camera is pointed too low!";
                if (angle.y > panoramaAngle.y / 2)
                    return "Camera is pointed too high!";
                top = (int)((angle.y + panoramaAngle.y / 2) * heightRange / panoramaAngle.y);
            }

            if (debug)
                printf("Snapping a photo at angle=%s, panoramaAngle=%s, pos=(%s, %s), camera=(%s, %s), composite=(%s, %s)",
                    angle,
                    panoramaAngle,
                    left,
                    top,
                    texture.width,
                    texture.height,
                    composite.width,
                    composite.height);


            for (int i = 0; i < texture.width; i++)
                snapped[(left + i) % composite.width] = true;

            int widthToWrite = texture.width;
            if (widthToWrite > composite.width - left)
                widthToWrite = composite.width - left;

            int heightToWrite = texture.height;
            if (heightToWrite > composite.height - top)
                heightToWrite = composite.height - top;

            Color[] colors = texture.GetPixels(0, 0, widthToWrite, heightToWrite);
            composite.SetPixels(left, top, widthToWrite, heightToWrite, colors);
            if (widthToWrite < texture.width)
            {
                left = 0;
                widthToWrite = texture.width - widthToWrite;

                colors = texture.GetPixels(0, 0, widthToWrite, heightToWrite);
                composite.SetPixels(left, top, widthToWrite, heightToWrite, colors);
            }
            composite.Apply();
            return null;
        }

        public int Progress()
        {
            int progress = 0;
            for (int i = 0; i < snapped.Length; i++)
                if (snapped[i])
                    progress++;
            return (int)(100 * progress / composite.width);
        }

        public Texture2D GetComposite()
        {
            return composite;
        }
    }

    private static void printf(string format, params object[] a)
    {
        int i = 0;
        string s = (format is string) ? System.Text.RegularExpressions.Regex.Replace((string)format, "%[sdi%]",
          match => match.Value == "%%" ? "%" : i < a.Length ? (a[i++] != null ? a[i - 1].ToString() : "null") : match.Value) : format.ToString();
        Debug.Log("ModuleScienceExperiment: " + s);
    }

    // Calculate angle from "forward", and optionally show debug
    private Vector2 CalculateAngles()
    {
        Vector3 east = part.transform.rotation.Inverse() * vessel.east;
        Vector3 up = part.transform.rotation.Inverse() * vessel.up;
        Vector3 north = part.transform.rotation.Inverse() * vessel.north;
        Vector3 forwardProjected = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
        Vector2 angles;

        if (reference == Vector3.zero || photoSize == panoramaSize)
        {
            reference = part.transform.rotation * forwardProjected;
            if (debug)
                printf("Recording new reference");
        }

        Vector3 referenceLocal = part.transform.rotation.Inverse() * reference;

        angles.x = Vector3.SignedAngle(referenceLocal, forwardProjected, Vector3.up);
        angles.y = Vector3.SignedAngle(forwardProjected, Vector3.forward, Vector3.Cross(forwardProjected, up));

        if (debug)
            printf("CalculateAngles: %s",
                angles);

        return angles;
    }

    private void UpdateDebugVectors()
    {
        if (debug)
        {
            Vector3 east = part.transform.rotation.Inverse() * vessel.east;
            Vector3 up = part.transform.rotation.Inverse() * vessel.up;
            Vector3 north = part.transform.rotation.Inverse() * vessel.north;
            Vector3 forwardProjected = Vector3.ProjectOnPlane(Vector3.forward, up).normalized;
            Vector3 referenceLocal = part.transform.rotation.Inverse() * reference;

            if (lineForward == null)
                lineForward = new LineInfo(part.transform, Color.magenta);
            lineForward.Update(Vector3.zero, Vector3.forward);

            if (lineForwardProjected == null)
                lineForwardProjected = new LineInfo(part.transform, Color.magenta);
            lineForwardProjected.Update(Vector3.zero, forwardProjected);

            if (lineUp == null)
                lineUp = new LineInfo(part.transform, Color.cyan);
            lineUp.Update(Vector3.zero, up);

            if (lineEast == null)
                lineEast = new LineInfo(part.transform, Color.blue);
            lineEast.Update(Vector3.zero, east);

            if (lineReference == null)
                lineReference = new LineInfo(part.transform, Color.white);
            lineReference.Update(Vector3.zero, referenceLocal);
        }
        else
        {
            if (lineForward != null)
            {
                lineForward.Destroy();
                lineForward = null;
            }
            if (lineForwardProjected != null)
            {
                lineForwardProjected.Destroy();
                lineForwardProjected = null;
            }
            if (lineUp != null)
            {
                lineUp.Destroy();
                lineUp = null;
            }
            if (lineEast != null)
            {
                lineEast.Destroy();
                lineEast = null;
            }
        }
    }

    private void FixedUpdate()
    {
        if (debug)
            printf("FixedUpdate");

        if (camera != null)
            camera.fieldOfView = fieldOfView;

        // Optionally create debug vectors
        UpdateDebugVectors();

        // Activate the display if needed
        UpdateDisplay();
    }

    Texture2D texture;

    private void SetStatus(string status)
    {
        this.status = status;
        if (dialogStatus != null)
            dialogStatus.SetOptionText(status);
    }

    // [KSPEvent(name = "TransmitData", guiName = "Transmit data", active = true, guiActive = true, requireFullControl = true)]
    private void TransmitData()
    {
        if (progress < 100)
        {
            SetStatus("Not enough data!");
            return;
        }

        photo.Clear();
        progress = 0;
        reference = Vector3.zero;
        DeployExperiment();
    }

    public override void OnStart(StartState state)
    {
        base.OnStart(state);

        if (photoSize.x > panoramaSize.x || photoSize.y > panoramaSize.y)
        {
            Debug.LogError("Single photo can't be larger than the panorma size!");
            return;
        }

        photo = new Photo(
            panoramaSize,
            panoramaAngles);

        isPanorama = photoSize != panoramaSize;

        if (isPanorama)
            Events["ViewPhoto"].guiName = "View panorama";
        else
            Events["ViewPhoto"].guiName = "View photo";


        // Precalculate a good starting point for the fov
        if (fieldOfView == -1)
        {
            if (isPanorama)
            {
                if (panoramaSize.x > panoramaSize.y)
                    fieldOfView = 360.0f * photoSize.x / panoramaSize.x;
                else
                    fieldOfView = 360.0f * photoSize.y / panoramaSize.y;
            }
            else
                fieldOfView = 100;
        }

        SetupRenderTexture();

        SetStatus("No data");

        // Hide the deploy experiment button
        foreach (BaseEvent ev in Events)
        {
            if (debug)
                printf("%s, %s, %s, %s, %s",
                    ev.guiName,
                    ev.id,
                    ev.name,
                    ev.active,
                    ev.guiActive
                    );

            switch (ev.name)
            {
                case "DeployExperiment":
                    experimentAction = ev;
                    ev.guiActive = false;
                    break;
            }
        }
    }

    public void CameraOnPostRender(Camera cam)
    {
        if (debug)
            printf("CameraOnPostRender &s", cam);

        if (cam != camera)
            return;

        if (!snap)
            return;

        snap = false;

        if (texture == null)
            texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();

        photo.Snap(CalculateAngles(), texture, debug);
        progress = photo.Progress();

        if (photo.Progress() < 100)
            return;

        SetStatus("Data acquired!");
    }

    private void SetupRenderTexture()
    {
        renderTexture = new RenderTexture((int)photoSize.x, (int)photoSize.y, 1);

        if (renderTexture == null)
        {
            printf("Failed to find renderTexture");
            return;
        }

        printf("renderTexture: %s", renderTexture);

        if (!renderTexture.Create())
        {
            printf("Failed to create renderTexture!");
            return;
        }

        GameObject cameraObject = new GameObject("cameraObject");

        if (cameraObject == null)
        {
            printf("Failed to create cameraObject");
            return;
        }

        cameraObject.transform.parent = part.transform;

        camera = cameraObject.AddComponent<Camera>();
        camera.name = "ModuleScienceExperimentCamera";
        Camera.onPostRender += CameraOnPostRender;

        if (camera == null)
        {
            printf("Failed to create camera");
            return;
        }

        camera.enabled = false;
        camera.targetTexture = renderTexture;

        Transform cameraTransform = part.FindModelTransform(cameraTransformName);

        if (cameraTransform)
        {
            printf("Using cameraTransform");
            camera.transform.position = cameraTransform.position;
            camera.transform.rotation = cameraTransform.rotation;
        }
        else
        {
            printf("Using part.transform");
            camera.transform.position = part.transform.position;
            camera.transform.rotation = part.transform.rotation;
        }

        SetDisplayTexture(renderTexture, Color.black);
    }

    private void SetDisplayTexture(Texture texture, Color color)
    {
        Renderer[] renderers = part.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer.name == displayMaterialName)
            {
                renderer.material.mainTexture = texture;
                renderer.material.color = color;

                printf("Updated texture in %s",
                    renderer.name);
            }
        }
    }

    // Show or hide the display
    private void UpdateDisplay()
    {
        if (displayWasActive == displayActive)
            return;

        displayWasActive = displayActive;
        if(camera != null)
            camera.enabled = displayActive;

        Renderer[] renderers = part.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer.name == displayMaterialName)
            {
                if (displayActive)
                    renderer.material.color = Color.white;
                else
                    renderer.material.color = Color.black;
            }
        }
    }
}
