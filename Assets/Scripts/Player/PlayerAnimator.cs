using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimator : MonoBehaviour
{
    private Animator animator;
    private PlayerMovement movement;

    private string currentClip = "";

    private void Awake()
    {
        animator = GetComponent<Animator>();
        movement = GetComponentInParent<PlayerMovement>();

        if (GetComponent<PlayerOutline>() == null)
            gameObject.AddComponent<PlayerOutline>();
    }

    private void Update()
    {
        string dir = GetDirection(movement.LastDirection);
        string prefix = movement.IsMoving ? "walk" : "idle";
        string clip = $"{prefix}_{dir}";

        if (clip != currentClip)
        {
            currentClip = clip;
            animator.Play(clip);
        }
    }

    private string GetDirection(Vector2 dir)
    {
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        // Normalize to 0-360
        if (angle < 0) angle += 360f;

        // 8 directions, each 45 degrees wide
        // East=0, NE=45, North=90, NW=135, West=180, SW=225, South=270, SE=315
        if (angle >= 337.5f || angle < 22.5f)  return "east";
        if (angle < 67.5f)                      return "north_east";
        if (angle < 112.5f)                     return "north";
        if (angle < 157.5f)                     return "north_west";
        if (angle < 202.5f)                     return "west";
        if (angle < 247.5f)                     return "south_west";
        if (angle < 292.5f)                     return "south";
        return                                         "south_east";
    }
}
