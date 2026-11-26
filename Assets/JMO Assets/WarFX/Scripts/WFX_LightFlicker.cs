using UnityEngine;
using System.Collections;

/**
 *	Rapidly sets a light on/off.
 *	
 *	(c) 2015, Jean Moreno
**/

[RequireComponent(typeof(Light))]
public class WFX_LightFlicker : MonoBehaviour
{
	public float time = 0.05f;
	public float duration = 1.0f;

    private float timer;
	
	void Start ()
	{
		timer = time;
		StartCoroutine("Flicker");
        Destroy(this.gameObject, duration);
    }
	
	IEnumerator Flicker()
	{
		while(true)
		{
			GetComponent<Light>().enabled = !GetComponent<Light>().enabled;
			
			do
			{
				timer -= Time.deltaTime;
				yield return null;
			}
			while(timer > 0);
			timer = time;
		}
	}
}
