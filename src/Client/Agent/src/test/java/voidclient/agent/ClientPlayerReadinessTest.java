package voidclient.agent;

import com.mojang.authlib.GameProfile;
import java.util.ArrayList;
import java.util.List;
import org.junit.Assert;
import org.junit.Test;

public final class ClientPlayerReadinessTest {
    @Test
    public void requiresExpectedPlayerInstalledInTheCurrentWorld() {
        Tracker.initialize(null, "expected");
        Client client = new Client();
        World current = new World();
        Player player = new Player("expected", current);
        Tracker.registerPlayer(player);
        Assert.assertNull(Tracker.activePlayer(client));
        client.player = player;
        client.world = current;
        Assert.assertNull(Tracker.activePlayer(client));
        current.players.add(player);
        Assert.assertSame(player, Tracker.activePlayer(client));
        client.world = new World();
        Assert.assertNull(Tracker.activePlayer(client));
        client.world = current;
        client.player = new Player("unrelated", current);
        current.players.add(client.player);
        Assert.assertNull(Tracker.activePlayer(client));
    }

    @Test
    public void baseConstructorObservationDoesNotCompleteReadiness() {
        Tracker.initialize(null, "expected");
        final Client client = new Client();
        client.world = new World();
        Player player = new Player("expected", client.world) {
            {
                Assert.assertNull(Tracker.activePlayer(client));
            }
        };
        client.player = player;
        client.world.players.add(player);
        Assert.assertSame(player, Tracker.activePlayer(client));
    }

    @Test
    public void readsTheUsernameTheGameWasLaunchedWith() {
        Assert.assertEquals("a1b2c3d4", Tracker.usernameFromCommand("net.minecraft.client.main.Main --version 1.21.1 --username a1b2c3d4 --uuid a1b2c3d4e5"));
        Assert.assertEquals("Steve", Tracker.usernameFromCommand("cpw.mods.bootstraplauncher.BootstrapLauncher --launchTarget forgeclient --username=Steve"));
        Assert.assertNull(Tracker.usernameFromCommand("net.minecraft.client.main.Main --version 1.21.1"));
        Assert.assertNull(Tracker.usernameFromCommand("net.minecraft.client.main.Main --username"));
        Assert.assertNull(Tracker.usernameFromCommand(null));
    }

    @Test
    public void acceptsTheClientsOwnPlayerWhenNoNameIsKnown() {
        Tracker.initialize(null, null);
        TargetingClient client = new TargetingClient();
        client.world = new World();
        Player other = new Player("other", client.world);
        client.world.players.add(other);
        client.target = other;
        Assert.assertNull(Tracker.activePlayer(client));
        Player own = new Player("generated", client.world);
        client.world.players.add(own);
        client.player = own;
        Assert.assertSame(own, Tracker.activePlayer(client));
        Assert.assertNull(Tracker.describeUnexpectedPlayer(client));
    }

    @Test
    public void describesAPlayerTheServerRenamed() {
        Tracker.initialize(null, "expected");
        Client client = new Client();
        client.world = new World();
        client.player = new Player("renamed", client.world);
        Assert.assertNull(Tracker.describeUnexpectedPlayer(client));
        client.world.players.add(client.player);
        Assert.assertNull(Tracker.activePlayer(client));
        Assert.assertEquals("Minecraft plays as renamed instead of the expected player expected", Tracker.describeUnexpectedPlayer(client));
    }

    public static class Client {
        public Player player;
        public World world;
    }
    public static class TargetingClient extends Client {
        public Object target;
    }
    public static class World {
        public final List<Player> players = new ArrayList<Player>();
    }
    public static class Player {
        public final GameProfile profile;
        public final World world;
        public Player(String name, World world) {
            this.profile = new GameProfile(name);
            this.world = world;
            Tracker.registerPlayer(this);
        }
    }
}
