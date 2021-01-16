This is a utility to help get the coordinates of leds in a light strip that are wrapped in a square/rectangle shape (ex. a tv).\
The list this generates can then be copied into the HueScreenAmbience config.

Manual adjustment of the cooridnates this generates may be necessary as its not assuming any bends in the light strip etc. but there is probably enough wiggle room to not matter.

&nbsp;

## Paramaters:

--light_count (number) (ex. 90)\
How many lights are are the light strip.

--light_start (number) (ex. 5)\
The light in sequence of the light strip that is at the top left of the rectangle.

--bottom_right (string) (ex. "(29,16)")\
The light coordinate of the bottom right light. This is used to determine the ratio of the light strip to figure out the position of the rest of the lights.\
For example if you are using a 16:9 display with a 90 led light strip you would have 29 lights on top and 16 on the right which is the coordinate you would pass in.

--border_decrease (float 0.0-1.0)\
An amount to move the coordinates of the lights in by. This is so the lights can sample not on the very edge of the provided image.\
ex. 0.9 will make (0,0) into (0.1,0.1) and (1.0,1.0) into (0.9,0.9), etc

--flipx (bool true/false)
Will flip the x coordinates calculated. this may be needed depending on direction light strip is run.

--flipy (bool true/false)
Will flip the y coordinates calculated. this may be needed depending on direction light strip is run.