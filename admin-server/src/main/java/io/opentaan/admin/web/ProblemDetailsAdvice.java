package io.opentaan.admin.web;

import java.util.Comparator;

import io.opentaan.admin.accounts.AccountNotFoundException;
import jakarta.validation.ConstraintViolation;
import jakarta.validation.ConstraintViolationException;
import org.springframework.http.HttpStatus;
import org.springframework.http.ProblemDetail;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;

/**
 * Maps exceptions to RFC 9457 problem details. Services throw exceptions that say
 * what went wrong; only this class knows which HTTP status that means.
 */
@RestControllerAdvice
class ProblemDetailsAdvice {

    @ExceptionHandler
    ProblemDetail accountNotFound(AccountNotFoundException exception) {
        var problem = ProblemDetail.forStatusAndDetail(HttpStatus.NOT_FOUND, exception.getMessage());
        problem.setTitle("Account not found");
        return problem;
    }

    /** A parameter broke a constraint declared in the OpenAPI contract, such as {@code minimum: 1}. */
    @ExceptionHandler
    ProblemDetail constraintViolated(ConstraintViolationException exception) {
        var detail = exception.getConstraintViolations().stream()
                .sorted(Comparator.comparing(violation -> violation.getPropertyPath().toString()))
                .map(ProblemDetailsAdvice::describe)
                .reduce((a, b) -> a + "; " + b)
                .orElse("Invalid request");
        var problem = ProblemDetail.forStatusAndDetail(HttpStatus.BAD_REQUEST, detail);
        problem.setTitle("Invalid request");
        return problem;
    }

    private static String describe(ConstraintViolation<?> violation) {
        var path = violation.getPropertyPath().toString();
        var parameter = path.substring(path.lastIndexOf('.') + 1);
        return parameter + " " + violation.getMessage();
    }
}
