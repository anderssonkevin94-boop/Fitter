// Minimal haptics bridge for Ravenholm Supply Co (GDD 20: optional haptics for delivery, craft, dispatch, discovery).
#import <UIKit/UIKit.h>

extern "C" void _RavenholmHaptic(int style)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (style >= 3)
        {
            UINotificationFeedbackGenerator *n = [[UINotificationFeedbackGenerator alloc] init];
            [n notificationOccurred:(style == 3 ? UINotificationFeedbackTypeSuccess : UINotificationFeedbackTypeWarning)];
            return;
        }
        UIImpactFeedbackStyle s = style == 0 ? UIImpactFeedbackStyleLight : style == 1 ? UIImpactFeedbackStyleMedium : UIImpactFeedbackStyleHeavy;
        UIImpactFeedbackGenerator *g = [[UIImpactFeedbackGenerator alloc] initWithStyle:s];
        [g impactOccurred];
    });
}
