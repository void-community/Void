package voidclient.agent;

import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Date;
import java.util.List;
import java.util.Map;
import java.util.WeakHashMap;

/**
 * Logs how each Minecraft connection ends, to the game's standard error, which the client keeps with a session's diagnostics. The game
 * takes a disconnect's reason only while the channel is open, and tells its listener a generic reason otherwise, so the lines tell which
 * thread saw what, and when, and the packets the connection read last before its channel went inactive.
 */
final class ConnectionTrace {
    static final String Read = "read";
    static final String Inactive = "inactive";
    static final String Exception = "exception";
    static final String Disconnect = "disconnect";
    static final int RememberedReads = 8;

    private static final Map<Object, Reads> reads = new WeakHashMap<Object, Reads>();

    private ConnectionTrace() {
    }

    public static void event(String event, Object connection, Object detail) {
        if (Read.equals(event)) {
            read(connection, detail);
            return;
        }

        if (Inactive.equals(event))
            print(connection, "channel inactive; read last: " + lastReads(connection));
        else if (Exception.equals(event))
            print(connection, "caught " + stackTrace(detail));
        else if (Disconnect.equals(event))
            print(connection, "disconnecting with \"" + JoinedConnection.reasonText(detail) + "\"");
    }

    /** Logs the reason the game tells the listener of a connection when it handles the connection's end. */
    static void told(Object connection, Object reason) {
        print(connection, "told its listener \"" + JoinedConnection.reasonText(reason) + "\"");
    }

    private static void read(Object connection, Object packet) {
        if (connection == null || packet == null)
            return;

        synchronized (reads) {
            Reads connectionReads = reads.get(connection);

            if (connectionReads == null) {
                connectionReads = new Reads();
                reads.put(connection, connectionReads);
            }

            connectionReads.add(packet.getClass().getName());
        }
    }

    static String lastReads(Object connection) {
        long now = System.nanoTime();

        synchronized (reads) {
            Reads connectionReads = reads.get(connection);
            return connectionReads == null ? "nothing" : connectionReads.describe(now);
        }
    }

    static String line(Object connection, String text) {
        return "Void client agent connection " + Integer.toHexString(System.identityHashCode(connection)) + " at "
            + new SimpleDateFormat("HH:mm:ss.SSS").format(new Date()) + " on " + Thread.currentThread().getName()
            + " (channel open: " + JoinedConnection.isOpen(connection) + "): " + text;
    }

    // The game logs what its connections catch only at debug level, if at all, while where it was thrown tells which code of the game failed
    static String stackTrace(Object detail) {
        if (!(detail instanceof Throwable))
            return String.valueOf(detail);

        java.io.StringWriter text = new java.io.StringWriter();
        ((Throwable) detail).printStackTrace(new java.io.PrintWriter(text));
        return text.toString().trim();
    }

    private static void print(Object connection, String text) {
        System.err.println(line(connection, text));
    }

    private static final class Reads {
        private final String[] names = new String[RememberedReads];
        private final long[] times = new long[RememberedReads];
        private long count;

        void add(String name) {
            int index = (int) (count++ % RememberedReads);
            names[index] = name;
            times[index] = System.nanoTime();
        }

        String describe(long now) {
            List<String> described = new ArrayList<String>();

            for (long index = Math.max(0, count - RememberedReads); index < count; index++) {
                int slot = (int) (index % RememberedReads);
                described.add(names[slot] + " " + (now - times[slot]) / 1000000 + " ms ago");
            }

            return count + " packets, the last of them " + described;
        }
    }
}
