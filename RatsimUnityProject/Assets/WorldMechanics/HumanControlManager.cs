using UnityEngine;

public class HumanControlManager : MonoBehaviour
{
    public bool humanControlEnabled = false;
    public string enableTopic = "/enable_human_control";
    
    public GameObject cameraObject;
    public Twist2DActuator velocityController;

    public void SetHumanControlEnabled(BoolMessage msg)
    {
        bool enbl = msg.data;
        humanControlEnabled = enbl;
        if (enbl)
        {
            cameraObject.SetActive(true);
            // Subscribe to the human control messages
        }
        else
        {
            cameraObject.SetActive(false);
            // Unsubscribe from the human control messages
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RoslikeTCPServer.GetInstance().RegisterTimerDiscrete(MainTimer, 1);
        RoslikeTCPServer.GetInstance().Subscribe<BoolMessage>(enableTopic, SetHumanControlEnabled);
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void MainTimer(TimerEvent ev)
    {
        if (humanControlEnabled)
        {
            ApplyHumanControls();
        }
    }

    public void ApplyHumanControls()
    {
        Twist2DMessage msg = new Twist2DMessage();
        msg.forward = Input.GetAxis("Vertical") * velocityController.maxLinearVelocity;
        //msg.left = Input.GetAxis("Horizontal");
        msg.radiansCounterClockwise = - Input.GetAxis("Horizontal") * velocityController.maxAngularVelocity;

        velocityController.OnTwist2DMessage(msg);
        //msg.radiansCounterClockwise = Input.GetAxis("Mouse X") * 0.1f;
    }
}
