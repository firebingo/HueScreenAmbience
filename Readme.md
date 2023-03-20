# Screen Ambience

This application can be used for various RGBlight effects from reading screens.

A basic level of HUE and HUE Entertainment groups are supported. RGB keyboards/mice/motherboards from Corsair/Logitech/Razer/Asus are supported although only Corsair has been tested.
Mice/motherboards only update from a average color of the whole screen. Keyboards can update according to the screen if a layout is defined for them. More information on this can be found at https://github.com/DarthAffe/RGB.NET.

LightStripClient can be built for and ran on a Raspberry Pi and will allow a connected Ws2812b light strip to be updated from another device. This is assuming that the light strip is connected via SPI. The PI also must have snd_bcm2835 disabled and on a pi3 `core_freq=250 core_freq_min=250` must be set in boot/config.txt, and on a pi4 these must be set to 500.
The coordinates for the light strip must be calculated manually. LightStripCalculator can be used to help calculate these for the purpose of wrapping a light strip around the edges/back of a tv.

HueScreenAmbience itself can also be built and ran on a Raspberry Pi. Hue devices and light strips can be updated this way. Other RGB devices like keyboard are not supported. This uses the Pi's CSI port to take in video so a HDMI->CSI module and other hardware such as a splitter or scaler may be necessary. Recommended to keep the output to the PI at 1280x720@30hz otherwise it may not be able to keep up with processing the video frames.
For updating light strips in this case the pi can either directly update a connected light strip or send packets over udp like a normal device to another Pi with a connected strip. If updating a connected light strip directly it has the same setup requirements as running LightStripClient.

If you are building the project yourself you will probably have to include the Asus RGB.net dll yourself as it doesn't load its dll properly.

If you are on a system with a integrated gpu using the Microsoft Hybrid system. Make sure the application is set to use the integrated gpu. And adapter will probably need to be set to 0.
