package voidclient.agent;

import java.lang.reflect.Constructor;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.junit.Assert;
import org.junit.Test;

public final class ModMenuRouteTest {
    private static final String Prefix = "voidclient/agent/ModMenuRouteTest$";

    @Test
    public void modMenuUsesTheRoutesOfTheMenuItExtends() {
        // A mod replaces the title screen with its own subclass, which the index of the game's code doesn't know (issue #1600).
        GameAutomationIndex.IndexedCode index = index("Title>Multiplayer", "Multiplayer>Direct");

        Assert.assertEquals(Prefix + "Multiplayer", GameAutomationController.transitions(index, ModMenu.class).get(0).targetScreenNames.get(0));
        Assert.assertEquals(Prefix + "Multiplayer", GameAutomationController.transitions(index, Title.class).get(0).targetScreenNames.get(0));
        Assert.assertTrue(GameAutomationController.transitions(index, Unrelated.class).isEmpty());
    }

    @Test
    public void opensTheNearestScreenOnTheRouteThatTakesOnlyAParent() {
        // The direct connect screen needs more than a parent, and the warning screen is one step farther than the multiplayer screen.
        GameAutomationIndex.IndexedCode index = index("Title>Safety", "Title>Multiplayer", "Safety>Multiplayer", "Multiplayer>Direct");
        Constructor<?> constructor = GameAutomationController.routeScreenConstructor(index, getClass().getClassLoader());

        Assert.assertNotNull(constructor);
        Assert.assertEquals(Multiplayer.class, constructor.getDeclaringClass());
    }

    @Test
    public void opensNothingWhenNoScreenOnTheRouteTakesOnlyAParent() {
        GameAutomationIndex.IndexedCode index = index("Title>ServerList", "ServerList>Direct", "Title>Unrelated");

        Assert.assertNull(GameAutomationController.routeScreenConstructor(index, getClass().getClassLoader()));
    }

    /**
     * Builds an index from routes such as {@code "Title>Multiplayer"}, where the right screen opens from the left one.
     */
    private static GameAutomationIndex.IndexedCode index(String... routes) {
        Map<String, List<TransitionPlan>> transitions = new HashMap<String, List<TransitionPlan>>();

        for (String route : routes) {
            String[] screens = route.split(">");
            List<TransitionPlan> from = transitions.get(Prefix + screens[0]);

            if (from == null) {
                from = new ArrayList<TransitionPlan>();
                transitions.put(Prefix + screens[0], from);
            }

            from.add(transition(Prefix + screens[1]));
        }

        Map<String, String> superTypes = new HashMap<String, String>();

        for (String name : new String[] { "Title", "Safety", "Multiplayer", "ServerList", "Direct", "Unrelated" })
            superTypes.put(Prefix + name, Prefix + "Screen");

        AutomationPlan plan = new AutomationPlan(Prefix + "Screen", Prefix + "Client", "set", Prefix + "Direct", null, null, null, null, null,
            null, transitions, Collections.<String[]>emptyList());
        return new GameAutomationIndex.IndexedCode(plan, superTypes);
    }

    private static TransitionPlan transition(String target) {
        return new TransitionPlan(Prefix + "Callback", "press", "()V", false, Collections.singletonList(target), false, false, null, null, null, false,
            new boolean[0]);
    }

    public static class Screen {
    }

    public static class Title extends Screen {
    }

    public static class ModMenu extends Title {
    }

    public static class Unrelated extends Screen {
    }

    public static class Safety extends Screen {
        public Safety(Screen parent) {
        }
    }

    public static class Multiplayer extends Screen {
        public Multiplayer(Screen parent) {
        }
    }

    public static class ServerList extends Screen {
        public ServerList(Screen parent, Object servers) {
        }
    }

    public static class Direct extends Screen {
        public Direct(Screen parent, Object callback, Object server) {
        }
    }
}
