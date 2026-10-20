package voidclient.agent;

import java.lang.invoke.MethodHandle;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.MethodType;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import org.junit.Assert;
import org.junit.Test;
import org.objectweb.asm.ClassWriter;
import org.objectweb.asm.MethodVisitor;
import org.objectweb.asm.Opcodes;

public final class FatalErrorReportTransformerTest {
    private static final List<Object[]> Reported = new ArrayList<Object[]>();

    @Test
    public void everyFatalErrorReachesTheAgentThroughTheBootstrapHooks() throws Throwable {
        byte[] transformed = new FatalErrorReportTransformer().transform(null, FatalErrorReportTransformer.ReportingName, null, null, reporting());
        Assert.assertNotNull(transformed);
        MethodHandle record = MethodHandles.lookup().findStatic(FatalErrorReportTransformerTest.class, "record",
            MethodType.methodType(void.class, Throwable.class, Path.class, Path.class));
        Path hooksJar = VoidClientAgent.writeGameHooksJar();

        try (URLClassLoader bootstrap = new URLClassLoader(new URL[] { hooksJar.toUri().toURL() }, null)) {
            bootstrap.loadClass(VoidClientAgent.GameHooksName.replace('/', '.'))
                .getMethod("install", MethodHandle.class, MethodHandle.class, MethodHandle.class, MethodHandle.class, MethodHandle.class)
                .invoke(null, null, null, null, null, record);
            Class<?> reporting = new BytecodeLoader(bootstrap).define(transformed);
            IllegalStateException error = new IllegalStateException("Loading errors encountered");
            Path gameDirectory = Paths.get("/root/.minecraft");
            Path crashReport = gameDirectory.resolve("crash-reports/crash-2026-10-20_06.19.30-client.txt");

            reporting.getMethod("reportFatalError", Throwable.class, Path.class, Path.class, Path.class).invoke(null, error, gameDirectory, null, crashReport);
            reporting.getMethod("reportFatalError", Throwable.class).invoke(null, error);

            Assert.assertEquals(1, Reported.size());
            Assert.assertArrayEquals(new Object[] { error, gameDirectory, crashReport }, Reported.get(0));
        } finally {
            Reported.clear();
            Files.delete(hooksJar);
        }
    }

    @Test
    public void reportIsSavedOnlyWhenTheLoaderSavedNone() throws Exception {
        Path gameDirectory = Files.createTempDirectory("void-client-fatal-error");

        try {
            Exception error = new IllegalStateException("Loading errors encountered:\n\t- Mod missingstub requires nonexistentmod 1 or above\n");
            FatalErrorReports.save(error, gameDirectory, null);
            List<Path> reports = reports(gameDirectory);

            Assert.assertEquals(1, reports.size());
            Assert.assertTrue(reports.get(0).getFileName().toString().matches("crash-\\d{4}-\\d{2}-\\d{2}_\\d{2}\\.\\d{2}\\.\\d{2}-fml\\.txt"));
            String report = new String(Files.readAllBytes(reports.get(0)), StandardCharsets.UTF_8);
            Assert.assertTrue(report.startsWith("---- Minecraft Crash Report ----\n"));
            Assert.assertTrue(report.contains("\nDescription: Mod loading error has occurred\n\njava.lang.IllegalStateException: Loading errors encountered:\n"
                + "\t- Mod missingstub requires nonexistentmod 1 or above\n"));

            Files.delete(reports.get(0));
            Path savedByLoader = Files.write(gameDirectory.resolve("crash-reports/crash-2026-10-20_06.19.30-client.txt"), new byte[0]);
            FatalErrorReports.save(error, gameDirectory, savedByLoader);

            Assert.assertEquals(1, reports(gameDirectory).size());
        } finally {
            try (Stream<Path> files = Files.walk(gameDirectory)) {
                for (Path file : files.sorted((first, second) -> second.compareTo(first)).collect(Collectors.toList()))
                    Files.delete(file);
            }
        }
    }

    public static void record(Throwable error, Path gameDirectory, Path crashReport) {
        Reported.add(new Object[] { error, gameDirectory, crashReport });
    }

    private static List<Path> reports(Path gameDirectory) throws Exception {
        try (Stream<Path> files = Files.list(gameDirectory.resolve("crash-reports"))) {
            return files.collect(Collectors.toList());
        }
    }

    /** Mirrors FancyModLoader 11, whose one-argument overload forwards to the overload the hook goes into. */
    private static byte[] reporting() {
        ClassWriter writer = new ClassWriter(ClassWriter.COMPUTE_FRAMES);
        writer.visit(Opcodes.V21, Opcodes.ACC_PUBLIC | Opcodes.ACC_FINAL, FatalErrorReportTransformer.ReportingName, null, "java/lang/Object", null);
        MethodVisitor full = writer.visitMethod(Opcodes.ACC_PUBLIC | Opcodes.ACC_STATIC, "reportFatalError",
            "(Ljava/lang/Throwable;Ljava/nio/file/Path;Ljava/nio/file/Path;Ljava/nio/file/Path;)V", null, null);
        full.visitCode();
        full.visitInsn(Opcodes.RETURN);
        full.visitMaxs(0, 0);
        full.visitEnd();
        MethodVisitor single = writer.visitMethod(Opcodes.ACC_PUBLIC | Opcodes.ACC_STATIC, "reportFatalError", "(Ljava/lang/Throwable;)V", null, null);
        single.visitCode();
        single.visitInsn(Opcodes.RETURN);
        single.visitMaxs(0, 0);
        single.visitEnd();
        writer.visitEnd();
        return writer.toByteArray();
    }

    private static class BytecodeLoader extends ClassLoader {
        BytecodeLoader(ClassLoader parent) {
            super(parent);
        }

        Class<?> define(byte[] bytes) {
            return defineClass(null, bytes, 0, bytes.length);
        }
    }
}
