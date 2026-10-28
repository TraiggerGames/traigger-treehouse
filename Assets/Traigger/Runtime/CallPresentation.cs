using UnityEngine;
namespace Traigger {
 public class CallPresentation:MonoBehaviour {
  void Update(){var controller=FindAnyObjectByType<StudioController>();if(!controller)return;if(Input.GetKeyDown(KeyCode.F8))controller.cleanOutput=!controller.cleanOutput;if(Input.GetKeyDown(KeyCode.Escape))controller.cleanOutput=false;}
 }
}
