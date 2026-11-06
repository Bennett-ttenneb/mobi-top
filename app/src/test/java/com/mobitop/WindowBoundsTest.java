package com.mobitop;

import static org.junit.Assert.assertTrue;

import org.junit.Test;

public class WindowBoundsTest {
    @Test
    public void windowsStayOnScreen() {
        int[][] screens = {{1920, 1032}, {1080, 2300}, {3840, 2112}};
        for (int[] s : screens) {
            for (int n = 0; n < 20; n++) {
                int[] r = DesktopActivity.windowBounds(s[0], s[1], n, 96);
                assertTrue(r[0] >= 0 && r[1] >= 0);
                assertTrue(r[2] <= s[0] && r[3] <= s[1]);
                assertTrue(r[2] > r[0] && r[3] > r[1]);
            }
        }
    }
}
