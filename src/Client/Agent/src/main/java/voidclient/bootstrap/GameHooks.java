package voidclient.bootstrap;

import java.lang.invoke.MethodHandle;
import java.nio.file.Path;

/**
 * Receives the calls the agent injects into game and mod classes. The build packages it alone in void-client-hooks.jar, which the agent's
 * Boot-Class-Path puts on the bootstrap class path before the game starts. Mod loaders such as NeoForge 1.21.1 define those classes in
 * loaders that can't see the agent jar, while every loader still delegates to the bootstrap loader, and Fabric only delegates classes
 * whose class file the platform class loader can see too. It must therefore depend on nothing but JDK classes.
 */
public final class GameHooks {
    private static volatile MethodHandle playerRegistration;
    private static volatile MethodHandle clientFrame;
    private static volatile MethodHandle connectionListener;
    private static volatile MethodHandle connectionRejection;
    private static volatile MethodHandle fatalError;

    private GameHooks() {
    }

    public static void install(MethodHandle player, MethodHandle frame, MethodHandle listener, MethodHandle rejection, MethodHandle fatal) {
        playerRegistration = player;
        clientFrame = frame;
        connectionListener = listener;
        connectionRejection = rejection;
        fatalError = fatal;
    }

    public static void fatalErrorReported(Throwable error, Path gameDirectory, Path crashReport) {
        MethodHandle target = fatalError;

        if (target == null)
            return;

        try {
            target.invokeExact(error, gameDirectory, crashReport);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }

    public static void registerPlayer(Object player) {
        MethodHandle target = playerRegistration;

        if (target == null)
            return;

        try {
            target.invokeExact(player);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }

    public static void applyClient(Object client) {
        MethodHandle target = clientFrame;

        if (target == null)
            return;

        try {
            target.invokeExact(client);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }

    public static void connectionListenerCreated(Object listener, Object connection) {
        MethodHandle target = connectionListener;

        if (target == null)
            return;

        try {
            target.invokeExact(listener, connection);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }

    public static void connectionRejected(Object listener, Object reason) {
        MethodHandle target = connectionRejection;

        if (target == null)
            return;

        try {
            target.invokeExact(listener, reason);
        } catch (Throwable exception) {
            // The agent only observes the game, so its failures must never break the game code that called it.
        }
    }
}
