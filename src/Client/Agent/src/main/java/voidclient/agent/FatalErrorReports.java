package voidclient.agent;

import java.io.PrintWriter;
import java.io.StringWriter;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.text.SimpleDateFormat;
import java.util.Date;

/**
 * Saves a crash report for a fatal error NeoForge's loader reports without one, such as a missing dependency, the way Forge and
 * older NeoForge versions did. The loader shows such errors only in its error window, which {@link FatalErrorWindowTransformer}
 * closes, so the report is what tells why the game ended.
 */
final class FatalErrorReports {
    private FatalErrorReports() {
    }

    static void save(Throwable error, Path gameDirectory, Path crashReport) {
        if (error == null || crashReport != null && Files.isRegularFile(crashReport))
            return;

        try {
            // The loader passes no game directory for errors before it found one, but it runs in the game directory.
            Path directory = (gameDirectory == null ? Paths.get(System.getProperty("user.dir")) : gameDirectory).resolve("crash-reports");
            Date now = new Date();
            StringWriter trace = new StringWriter();
            error.printStackTrace(new PrintWriter(trace));
            String report = "---- Minecraft Crash Report ----\n"
                + "// The Void client saved this report in place of NeoForge's fatal error window, which nobody can answer in the container.\n\n"
                + "Time: " + new SimpleDateFormat("yyyy-MM-dd HH:mm:ss").format(now) + "\n"
                + "Description: Mod loading error has occurred\n\n"
                + trace;
            Path file = directory.resolve("crash-" + new SimpleDateFormat("yyyy-MM-dd_HH.mm.ss").format(now) + "-fml.txt");

            Files.createDirectories(directory);
            Files.write(file, report.getBytes(StandardCharsets.UTF_8));
            System.err.println("Void client agent saved NeoForge's fatal error to " + file);
        } catch (Throwable exception) {
            System.err.println("Void client agent could not save NeoForge's fatal error: " + exception);
        }
    }
}
